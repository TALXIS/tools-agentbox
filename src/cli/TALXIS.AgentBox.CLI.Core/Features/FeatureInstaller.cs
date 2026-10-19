using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core.Host;

namespace TALXIS.AgentBox.CLI.Core.Features;

/// <summary>
/// Installs devcontainer Features on this Linux machine without the devcontainer CLI: fetch each
/// locked Feature from its OCI registry by digest, run its install.sh, apply its containerEnv, and
/// add the same /usr/local/bin wrappers as src/container/scripts/install-features.sh (ported from it),
/// so non-login shells (agent tool calls, docker exec) find dotnet, dnx, pac, txc and node.
/// Must run as root: Features install system packages.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class FeatureInstaller(IProcessRunner processes, HttpClient http, ILogger logger)
{
    private const string ProfileScript = "/etc/profile.d/agentbox-features.sh";
    private const string KeyringLine = "command -v agentbox-keyring-init >/dev/null 2>&1 && eval \"$(agentbox-keyring-init)\"";

    public async Task<IReadOnlyList<(LockedFeature Feature, bool Succeeded, string? Error)>> InstallAsync(
        IReadOnlyList<LockedFeature> features, CancellationToken cancellationToken)
    {
        var results = new List<(LockedFeature, bool, string?)>();
        var workRoot = Directory.CreateTempSubdirectory("agentbox-features-");
        // CreateTempSubdirectory makes it 0700, but Features run steps as the remote user from their
        // own directory (su <user> -c ...), which then can't even enter it.
        File.SetUnixFileMode(workRoot.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try
        {
            for (var i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                logger.LogInformation("Installing Feature {Feature} ({Digest})", feature.Path, feature.Digest);
                try
                {
                    var directory = Path.Combine(workRoot.FullName, $"feat-{i}");
                    await DownloadAsync(feature, directory, cancellationToken).ConfigureAwait(false);
                    var run = await processes.RunAsync("bash", ["install.sh"], TimeSpan.FromMinutes(20), OptionEnvironment(feature), directory, cancellationToken).ConfigureAwait(false);
                    if (!run.Succeeded)
                    {
                        var tail = string.Join('\n', (run.StandardError + run.StandardOutput).Split('\n').TakeLast(15));
                        logger.LogWarning("Feature {Feature} failed (exit {ExitCode}):\n{Output}", feature.Path, run.ExitCode, tail);
                        results.Add((feature, false, $"install.sh exit code {run.ExitCode}"));
                        continue;
                    }
                    ApplyContainerEnv(Path.Combine(directory, "devcontainer-feature.json"));
                    WriteDotnetWrapperIfPossible();
                    results.Add((feature, true, null));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
                {
                    logger.LogWarning(ex, "Feature {Feature} failed: {Error}", feature.Path, ex.Message);
                    results.Add((feature, false, ex.Message));
                }
            }
            WritePostInstallWrappers();
            return results;
        }
        finally
        {
            workRoot.Delete(recursive: true);
        }
    }

    private async Task DownloadAsync(LockedFeature feature, string directory, CancellationToken cancellationToken)
    {
        var tokenJson = await http.GetStringAsync(
            $"https://{feature.Registry}/token?service={feature.Registry}&scope=repository:{feature.Path}:pull", cancellationToken).ConfigureAwait(false);
        var token = JsonDocument.Parse(tokenJson).RootElement.GetProperty("token").GetString();

        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{feature.Registry}/v2/{feature.Path}/manifests/{feature.Digest}");
        manifestRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        manifestRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
        using var manifestResponse = await http.SendAsync(manifestRequest, cancellationToken).ConfigureAwait(false);
        manifestResponse.EnsureSuccessStatusCode();
        var manifest = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var layerDigest = manifest.RootElement.GetProperty("layers")[0].GetProperty("digest").GetString();

        using var blobRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{feature.Registry}/v2/{feature.Path}/blobs/{layerDigest}");
        blobRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var blobResponse = await http.SendAsync(blobRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        blobResponse.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await blobResponse.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        Directory.CreateDirectory(directory);
        var isGzip = buffer.Length > 2 && buffer.GetBuffer()[0] == 0x1f && buffer.GetBuffer()[1] == 0x8b;
        Stream tar = isGzip ? new GZipStream(buffer, CompressionMode.Decompress) : buffer;
        await using (tar.ConfigureAwait(false))
            await TarFile.ExtractToDirectoryAsync(tar, directory, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Feature options become upper-cased env vars (as install-features.sh and the devcontainer CLI do).</summary>
    internal static Dictionary<string, string?> OptionEnvironment(LockedFeature feature)
    {
        // Run through sudo, the Features still set up user-scope bits for the user who asked.
        var (user, home) = Elevation.InvokingUser();
        var environment = new Dictionary<string, string?>
        {
            ["_CONTAINER_USER"] = user,
            ["_REMOTE_USER"] = user,
            ["_REMOTE_USER_HOME"] = home,
        };
        foreach (var (key, value) in feature.Options ?? [])
        {
            var name = InvalidEnvChars().Replace(key.ToUpperInvariant(), "_");
            name = LeadingDigitsOrUnderscores().Replace(name, "_");
            environment[name] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return environment;
    }

    /// <summary>Apply a Feature's containerEnv to this process (later Features see it) and to login shells.</summary>
    private static void ApplyContainerEnv(string featureJsonPath)
    {
        if (!File.Exists(featureJsonPath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(featureJsonPath));
        if (!document.RootElement.TryGetProperty("containerEnv", out var containerEnv))
            return;
        // Reinstalls must not pile up duplicate lines.
        var existing = File.Exists(ProfileScript) ? File.ReadAllLines(ProfileScript).ToHashSet() : [];
        var lines = new StringBuilder();
        foreach (var variable in containerEnv.EnumerateObject())
        {
            var raw = variable.Value.GetString() ?? "";
            var expanded = EnvReference().Replace(raw, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
            Environment.SetEnvironmentVariable(variable.Name, expanded);
            var line = $"export {variable.Name}=\"{raw}\"";
            if (existing.Add(line))
                lines.AppendLine(line);
        }
        File.AppendAllText(ProfileScript, lines.ToString());
    }

    private static void WriteDotnetWrapperIfPossible()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && File.Exists(Path.Combine(dotnetRoot, "dotnet")))
            WriteDotnetWrapper("dotnet", Path.Combine(dotnetRoot, "dotnet"), keyring: false);
    }

    /// <summary>Same wrappers and symlinks install-features.sh writes after its Feature loop.</summary>
    private static void WritePostInstallWrappers()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && File.Exists(Path.Combine(dotnetRoot, "dnx")))
            WriteDotnetWrapper("dnx", Path.Combine(dotnetRoot, "dnx"), keyring: false);

        // The dotnet-tool Features install into the invoking user's home (see OptionEnvironment).
        var tools = Path.Combine(Elevation.InvokingUser().Home, ".dotnet", "tools");
        foreach (var tool in new[] { "pac", "txc" })
        {
            if (File.Exists(Path.Combine(tools, tool)))
                WriteDotnetWrapper(tool, Path.Combine(tools, tool), keyring: true);
        }

        // The node Feature installs via nvm, which only reaches PATH in login shells; claude-code
        // installs into the same nvm prefix.
        var nvmCurrent = Path.Combine(Environment.GetEnvironmentVariable("NVM_DIR") ?? "/usr/local/share/nvm", "current", "bin");
        if (!File.Exists(Path.Combine(nvmCurrent, "node")))
            return;
        foreach (var binary in new[] { "node", "npm", "npx", "corepack", "claude" })
        {
            var target = Path.Combine(nvmCurrent, binary);
            if (!File.Exists(target))
                continue;
            var link = Path.Combine("/usr/local/bin", binary);
            if (File.Exists(link) || Directory.Exists(link) || new FileInfo(link).LinkTarget is not null)
                File.Delete(link);
            File.CreateSymbolicLink(link, target);
        }
    }

    private static void WriteDotnetWrapper(string name, string realPath, bool keyring)
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "";
        var script = new StringBuilder()
            .Append("#!/bin/bash\n")
            .Append("export DOTNET_ROOT=\"").Append(dotnetRoot).Append("\"\n")
            .Append(keyring ? KeyringLine + "\n" : "")
            .Append("exec \"").Append(realPath).Append("\" \"$@\"\n")
            .ToString();
        var path = Path.Combine("/usr/local/bin", name);
        File.WriteAllText(path, script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    [GeneratedRegex("[^A-Z0-9_]")]
    private static partial Regex InvalidEnvChars();

    [GeneratedRegex("^[0-9_]+")]
    private static partial Regex LeadingDigitsOrUnderscores();

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvReference();
}
