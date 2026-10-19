using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Features;

public sealed record FeatureResult(LockedFeature Feature, bool Succeeded, string? Error = null);

public interface IFeatureInstaller
{
    Task<IReadOnlyList<FeatureResult>> InstallAsync(IReadOnlyList<LockedFeature> features, CancellationToken cancellationToken);
}

/// <summary>
/// Installs devcontainer Features on this machine the way the devcontainer CLI does, without
/// Docker: fetch each pinned Feature from its registry, check it against its pinned digest, run its
/// install.sh with its options, and apply its containerEnv. Needs root.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class FeatureInstaller(IProcessRunner processes, HttpClient http, ILogger logger) : IFeatureInstaller
{
    private const string ProfileScript = "/etc/profile.d/agentbox-features.sh";
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);
    private const int DownloadAttempts = 3;
    internal static readonly UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public async Task<IReadOnlyList<FeatureResult>> InstallAsync(IReadOnlyList<LockedFeature> features, CancellationToken cancellationToken)
    {
        var workRoot = Directory.CreateTempSubdirectory("agentbox-features-");
        // Features run some steps as the remote user from their own directory (su <user> -c ...).
        File.SetUnixFileMode(workRoot.FullName, Executable);
        // Download everything at once; install strictly in the locked order.
        var downloads = features
            .Select((feature, i) => DownloadAsync(feature, Path.Combine(workRoot.FullName, $"feat-{i}"), cancellationToken))
            .ToList();
        try
        {
            var results = new List<FeatureResult>();
            for (var i = 0; i < features.Count; i++)
            {
                results.Add(await InstallOneAsync(features[i], downloads[i], cancellationToken).ConfigureAwait(false));
                // After each one: later Features run tools as the remote user (su resets PATH).
                try
                {
                    NonLoginShellLinks.Write();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Couldn't write the /usr/local/bin entries: {Error}", ex.Message);
                }
            }
            return results;
        }
        finally
        {
            // Downloads still running would recreate their directories after the cleanup.
            try
            {
                await Task.WhenAll(downloads).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Each failure is already in that Feature's result.
            }
            workRoot.Delete(recursive: true);
        }
    }

    private async Task<FeatureResult> InstallOneAsync(LockedFeature feature, Task<string> download, CancellationToken cancellationToken)
    {
        logger.LogInformation("Installing Feature {Feature}", feature.Path);
        try
        {
            var directory = await download.ConfigureAwait(false);
            var featureJson = Path.Combine(directory, "devcontainer-feature.json");
            if (!File.Exists(featureJson))
                throw new InvalidDataException("the Feature has no devcontainer-feature.json");
            var run = await processes.RunAsync("bash", ["install.sh"], InstallTimeout, OptionEnvironment(feature, DeclaredDefaults(featureJson)), directory, cancellationToken).ConfigureAwait(false);
            if (!run.Succeeded)
            {
                logger.LogWarning("Feature {Feature} failed: {Reason}\n{Output}\n{Errors}", feature.Path, run.FailureSummary,
                    Tail(run.StandardOutput), Tail(run.StandardError));
                return new(feature, false, run.TimedOut ? "timed out" : $"install.sh exit code {run.ExitCode}");
            }
            ApplyContainerEnv(featureJson);
            return new(feature, true);
        }
        // Whatever goes wrong with one Feature is that Feature's failure; only the caller's cancel stops the run.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Feature {Feature} failed: {Error}", feature.Path, ex.Message);
            return new(feature, false, ex.Message);
        }
    }

    internal async Task<string> DownloadAsync(LockedFeature feature, string directory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(feature, directory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < DownloadAttempts && !cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogDebug("Downloading {Feature} failed, retrying: {Error}", feature.Path, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> DownloadOnceAsync(LockedFeature feature, string directory, CancellationToken cancellationToken)
    {
        var tokenJson = await http.GetStringAsync(
            $"https://{feature.Registry}/token?service={feature.Registry}&scope=repository:{feature.Path}:pull", cancellationToken).ConfigureAwait(false);
        var token = new AuthenticationHeaderValue("Bearer", JsonDocument.Parse(tokenJson).RootElement.GetProperty("token").GetString());

        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{feature.Registry}/v2/{feature.Path}/manifests/{feature.Digest}");
        manifestRequest.Headers.Authorization = token;
        manifestRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
        using var manifestResponse = (await http.SendAsync(manifestRequest, cancellationToken).ConfigureAwait(false)).EnsureSuccessStatusCode();
        var manifestBytes = await manifestResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        VerifyDigest(manifestBytes, feature.Digest, "manifest");
        using var manifest = JsonDocument.Parse(manifestBytes);
        var layer = manifest.RootElement.GetProperty("layers")[0].GetProperty("digest").GetString();

        using var blobRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{feature.Registry}/v2/{feature.Path}/blobs/{layer}");
        blobRequest.Headers.Authorization = token;
        using var blobResponse = (await http.SendAsync(blobRequest, cancellationToken).ConfigureAwait(false)).EnsureSuccessStatusCode();
        var bytes = await blobResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        VerifyDigest(bytes, layer, "layer");

        Directory.CreateDirectory(directory);
        var gzip = bytes is [0x1f, 0x8b, ..];
        await using var tar = gzip ? new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress) : (Stream)new MemoryStream(bytes);
        await TarFile.ExtractToDirectoryAsync(tar, directory, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
        return directory;
    }

    private static string Tail(string text) => string.Join('\n', text.TrimEnd().Split('\n').TakeLast(10));

    /// <summary>What runs as root must be exactly what the lockfile pinned; the manifest pins the layer.</summary>
    internal static void VerifyDigest(byte[] content, string? digest, string what)
    {
        var actual = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(actual, digest, StringComparison.Ordinal))
            throw new InvalidDataException($"the {what} doesn't match its pinned digest {digest}");
    }

    /// <summary>Each option's declared default: the devcontainer CLI passes them, and install.sh
    /// scripts rely on it (git with an empty VERSION falls through to a source build).</summary>
    internal static Dictionary<string, JsonElement> DeclaredDefaults(string featureJsonPath)
    {
        var defaults = new Dictionary<string, JsonElement>();
        if (!File.Exists(featureJsonPath))
            return defaults;
        using var document = JsonDocument.Parse(File.ReadAllText(featureJsonPath));
        if (document.RootElement.TryGetProperty("options", out var options))
        {
            foreach (var option in options.EnumerateObject().Where(o => o.Value.TryGetProperty("default", out _)))
                defaults[option.Name] = option.Value.GetProperty("default").Clone();
        }
        return defaults;
    }

    /// <summary>Options become upper-cased environment variables, as the devcontainer CLI passes them.</summary>
    internal static Dictionary<string, string?> OptionEnvironment(LockedFeature feature, IReadOnlyDictionary<string, JsonElement> defaults)
    {
        // Under sudo, Features still set up per-user parts for the person who asked.
        var (user, home) = Sudo.InvokingUser();
        var environment = new Dictionary<string, string?>
        {
            ["_CONTAINER_USER"] = user,
            ["_REMOTE_USER"] = user,
            ["_REMOTE_USER_HOME"] = home,
        };
        var options = new Dictionary<string, JsonElement>(defaults);
        foreach (var (key, value) in feature.Options ?? [])
            options[key] = value;
        foreach (var (key, value) in options)
        {
            var name = LeadingDigitsOrUnderscores().Replace(InvalidEnvChars().Replace(key.ToUpperInvariant(), "_"), "_");
            environment[name] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return environment;
    }

    /// <summary>Applies a Feature's containerEnv to this process, so later Features see it, and to login shells.</summary>
    private static void ApplyContainerEnv(string featureJsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(featureJsonPath));
        if (!document.RootElement.TryGetProperty("containerEnv", out var containerEnv))
            return;
        var existing = File.Exists(ProfileScript) ? File.ReadAllLines(ProfileScript).ToHashSet() : [];
        var added = new StringBuilder();
        foreach (var variable in containerEnv.EnumerateObject())
        {
            if (!EnvName().IsMatch(variable.Name))
                throw new InvalidDataException($"containerEnv has an invalid variable name '{variable.Name}'");
            var raw = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString()! : variable.Value.GetRawText();
            Environment.SetEnvironmentVariable(variable.Name, EnvReference().Replace(raw, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? ""));
            var line = $"export {variable.Name}=\"{ShellDoubleQuoted(raw)}\"";
            if (existing.Add(line))
                added.AppendLine(line);
        }
        File.AppendAllText(ProfileScript, added.ToString());
    }

    /// <summary>The value inside double quotes in a shell script: literal, except <c>${NAME}</c> references.</summary>
    internal static string ShellDoubleQuoted(string value) =>
        LoneDollar().Replace(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`"), "\\$");

    [GeneratedRegex("[^A-Z0-9_]")]
    private static partial Regex InvalidEnvChars();

    [GeneratedRegex("^[0-9_]+")]
    private static partial Regex LeadingDigitsOrUnderscores();

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvReference();

    [GeneratedRegex(@"\$(?!\{[A-Za-z_][A-Za-z0-9_]*\})")]
    private static partial Regex LoneDollar();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvName();
}

/// <summary>
/// Agents, CI steps and <c>docker run</c> use non-login shells, which never read /etc/profile.d, so
/// tools installed under DOTNET_ROOT or nvm aren't on their PATH. These /usr/local/bin entries fix that.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class NonLoginShellLinks
{
    public static void Write()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            foreach (var tool in new[] { "dotnet", "dnx" })
                WriteWrapper(tool, Path.Combine(dotnetRoot, tool), dotnetRoot);
        }

        // nvm is only on PATH in login shells; Claude Code installs into the same prefix.
        var nvm = Path.Combine(Environment.GetEnvironmentVariable("NVM_DIR") ?? "/usr/local/share/nvm", "current", "bin");
        foreach (var binary in new[] { "node", "npm", "npx", "corepack", "claude" })
        {
            var target = Path.Combine(nvm, binary);
            if (!File.Exists(target))
                continue;
            var link = Path.Combine("/usr/local/bin", binary);
            File.Delete(link);
            File.CreateSymbolicLink(link, target);
        }
    }

    private static void WriteWrapper(string name, string target, string dotnetRoot)
    {
        if (!File.Exists(target))
            return;
        var path = Path.Combine("/usr/local/bin", name);
        if (Path.GetFullPath(target) == path)
            return;
        File.Delete(path); // writing would follow a symlink to the real executable and overwrite it
        File.WriteAllText(path, $"#!/bin/bash\nexport DOTNET_ROOT=\"{dotnetRoot}\"\nexec \"{target}\" \"$@\"\n");
        File.SetUnixFileMode(path, FeatureInstaller.Executable);
    }
}
