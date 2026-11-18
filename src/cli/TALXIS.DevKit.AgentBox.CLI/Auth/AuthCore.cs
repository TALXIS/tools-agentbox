using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Auth;

internal static class AadAuth
{
    public const string ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    public const int Port = 50343;
    public const string EndpointPath = "/metadata/identity/oauth2/token";
    public const string DefaultTenant = "organizations";

    public static string Authority(string? tenantId = null)
    {
        var tenant = string.IsNullOrWhiteSpace(tenantId) ? DefaultTenant : tenantId.Trim();
        if (tenant.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
            throw new InvalidInputException("Tenant ID must be a tenant GUID or domain name.");
        return $"https://login.microsoftonline.com/{tenant}";
    }

    public static string ResourceToScope(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource)
            || (!Guid.TryParse(resource, out _) && !Uri.TryCreate(resource, UriKind.Absolute, out _)))
            throw new InvalidInputException("Resource must be an absolute URI or a GUID.");
        if (resource.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
            throw new InvalidInputException("Provide a resource URI or GUID, not an MSAL scope.");
        return $"{resource}/.default";
    }

    public static string ScopeForLogin(string? resource) =>
        string.IsNullOrWhiteSpace(resource)
            ? "https://management.azure.com//.default"
            : ResourceToScope(resource);
}

public sealed record ActiveAccount(string HomeAccountId, string Username, string TenantId);

public sealed class ActiveAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = AgentBoxOutputJsonOptions.Default;
    private readonly string _directory;
    private readonly string _path;

    public ActiveAccountStore(string homeDirectory)
    {
        _directory = Path.Combine(homeDirectory, ".agentbox");
        _path = Path.Combine(_directory, "auth.json");
    }

    public string FilePath => _path;

    public ActiveAccount? Read()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<ActiveAccount>(stream, JsonOptions)
                ?? throw new InvalidDataException("The active account file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The active account file is invalid.", ex);
        }
    }

    public void Write(ActiveAccount account)
    {
        PrivateFiles.EnsureDirectory(_directory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(account, JsonOptions);
            using (var stream = new FileStream(temporary, PrivateFiles.FileOptions(FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

internal static class PrivateFiles
{
    public const UnixFileMode OwnerFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static FileStreamOptions FileOptions(FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerFileMode;
        return options;
    }
}

internal sealed class AuthService(IHostInfo host, IProcessRunner processes, ILogger logger)
{
    private readonly ActiveAccountStore _accounts = new(host.HomeDirectory);

    public async Task<AuthenticationResult> LoginAsync(string? tenantId, string? resource, CancellationToken cancellationToken)
    {
        var app = await MsalApplicationFactory.CreateAsync(host, processes, logger, cancellationToken).ConfigureAwait(false);
        var request = app.AcquireTokenWithDeviceCode([AadAuth.ScopeForLogin(resource)], code =>
        {
            OutputWriter.WriteErrorLine(code.Message);
            return Task.CompletedTask;
        });
        if (!string.IsNullOrWhiteSpace(tenantId))
            request = request.WithTenantId(tenantId.Trim());
        var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        var homeAccountId = result.Account?.HomeAccountId.Identifier
            ?? throw new InvalidOperationException("MSAL completed device-code authentication without an account.");
        _accounts.Write(new ActiveAccount(homeAccountId, result.Account.Username ?? "", result.TenantId));
        return result;
    }

    public async Task<ITokenProvider> CreateTokenProviderAsync(CancellationToken cancellationToken)
    {
        var app = await MsalApplicationFactory.CreateAsync(host, processes, logger, cancellationToken).ConfigureAwait(false);
        return new MsalTokenProvider(app, _accounts);
    }

    public async Task<ActiveAccount?> GetActiveAccountAsync(CancellationToken cancellationToken)
    {
        var active = _accounts.Read();
        if (active is null)
            return null;

        var app = await MsalApplicationFactory.CreateAsync(host, processes, logger, cancellationToken).ConfigureAwait(false);
        return await app.GetAccountAsync(active.HomeAccountId).ConfigureAwait(false) is null ? null : active;
    }

    public ActiveAccountStore AccountStore => _accounts;
}

internal static class MsalApplicationFactory
{
    private const string CacheFileName = "msal.cache";
    private const string KeyringSchema = "com.talxis.agentbox";
    private static readonly KeyValuePair<string, string> KeyringAttribute1 = new("Product", "AgentBox");
    private static readonly KeyValuePair<string, string> KeyringAttribute2 = new("Version", "1");

    public static async Task<IPublicClientApplication> CreateAsync(
        IHostInfo host,
        IProcessRunner processes,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(host.HomeDirectory, ".agentbox");
        PrivateFiles.EnsureDirectory(directory);

        if (host.Os == OsKind.Linux)
            await InitializeLinuxKeyringAsync(processes, logger, cancellationToken).ConfigureAwait(false);

        var storage = new StorageCreationPropertiesBuilder(CacheFileName, directory);
        if (host.Os == OsKind.MacOS)
            storage.WithMacKeyChain("AgentBox MSAL Cache", AadAuth.ClientId);
        if (host.Os == OsKind.Linux)
            storage.WithLinuxKeyring(KeyringSchema, "default", "AgentBox authentication cache", KeyringAttribute1, KeyringAttribute2);

        var cache = host.Os == OsKind.Linux
            ? await CreateLinuxCacheAsync(storage, directory, logger, cancellationToken).ConfigureAwait(false)
            : await CreateAndVerifyAsync(storage.Build()).ConfigureAwait(false);

        var app = PublicClientApplicationBuilder.Create(AadAuth.ClientId)
            .WithAuthority(AadAuth.Authority())
            .Build();
        cache.RegisterCache(app.UserTokenCache);
        cancellationToken.ThrowIfCancellationRequested();
        return app;
    }

    private static async Task<MsalCacheHelper> CreateAndVerifyAsync(StorageCreationProperties properties)
    {
        var helper = await MsalCacheHelper.CreateAsync(properties).ConfigureAwait(false);
        helper.VerifyPersistence();
        return helper;
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is MsalCachePersistenceException or DllNotFoundException or TypeInitializationException
        || exception.InnerException is not null && IsPersistenceFailure(exception.InnerException);

    private static async Task<MsalCacheHelper> CreateLinuxCacheAsync(
        StorageCreationPropertiesBuilder secureBuilder,
        string directory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(directory, "msal-cache-backend");
        await using var selectionLock = await AcquireCacheSelectionLockAsync(Path.Combine(directory, "msal-cache-backend.lock"), cancellationToken).ConfigureAwait(false);
        var selected = File.Exists(markerPath) ? (await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false)).Trim() : null;

        if (selected == "plaintext")
        {
            var fallback = new StorageCreationPropertiesBuilder(CacheFileName, directory)
                .WithLinuxUnprotectedFile()
                .Build();
            return await CreateAndVerifyAsync(fallback).ConfigureAwait(false);
        }

        if (selected is not null and not "keyring")
            throw new InvalidDataException("The AgentBox MSAL cache backend marker is invalid.");

        try
        {
            var helper = await CreateAndVerifyAsync(secureBuilder.Build()).ConfigureAwait(false);
            if (selected is null)
                WritePrivateTextAtomically(markerPath, "keyring");
            return helper;
        }
        catch (Exception ex) when (selected is null && IsPersistenceFailure(ex))
        {
            logger.LogWarning("Secure Linux token-cache storage is unavailable; using the approved owner-only plaintext fallback.");
            var fallback = new StorageCreationPropertiesBuilder(CacheFileName, directory)
                .WithLinuxUnprotectedFile()
                .Build();
            var helper = await CreateAndVerifyAsync(fallback).ConfigureAwait(false);
            WritePrivateTextAtomically(markerPath, "plaintext");
            return helper;
        }
    }

    private static async Task<FileStream> AcquireCacheSelectionLockAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, PrivateFiles.FileOptions(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Timed out selecting the AgentBox Linux MSAL cache backend.");
        }
    }

    private static void WritePrivateTextAtomically(string path, string value)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, PrivateFiles.FileOptions(FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task InitializeLinuxKeyringAsync(IProcessRunner processes, ILogger logger, CancellationToken cancellationToken)
    {
        var command = processes.FindOnPath("agentbox-keyring-init");
        if (command is null)
            return;

        var result = await processes.RunAsync(
            command, [], TimeSpan.FromSeconds(15), cancellationToken: cancellationToken, showErrors: true).ConfigureAwait(false);
        var prefix = "export DBUS_SESSION_BUS_ADDRESS='";
        var output = result.StandardOutput.Trim();
        if (result.Succeeded && output.StartsWith(prefix, StringComparison.Ordinal) && output.EndsWith('\''))
        {
            var address = output[prefix.Length..^1];
            if (address.StartsWith("unix:path=", StringComparison.Ordinal) && !address.Contains('\''))
            {
                Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", address);
                return;
            }
        }

        logger.LogWarning("Could not initialize the AgentBox Linux keyring environment; secure-cache verification will determine whether plaintext fallback is needed.");
    }
}

internal sealed class NoAuthenticatedAccountException : Exception
{
}

internal sealed class MsalTokenProvider(IPublicClientApplication app, ActiveAccountStore accountStore) : ITokenProvider
{
    public async Task<IdentityToken> AcquireAsync(string resource, CancellationToken cancellationToken)
    {
        var active = accountStore.Read() ?? throw new NoAuthenticatedAccountException();
        var account = await app.GetAccountAsync(active.HomeAccountId).ConfigureAwait(false)
            ?? throw new NoAuthenticatedAccountException();

        var result = await app.AcquireTokenSilent([AadAuth.ResourceToScope(resource)], account)
            .WithTenantId(active.TenantId)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new IdentityToken(
            result.AccessToken,
            result.ExpiresOn,
            resource,
            "Bearer",
            result.CorrelationId.ToString());
    }
}
