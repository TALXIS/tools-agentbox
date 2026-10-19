using System.Text.Json;
using NuGet.Versioning;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Registry;

namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

public enum PrerequisiteStatus
{
    Ok = 0,
    Missing = 1,
    Outdated = 2,
    Broken = 3,
    NotApplicable = 4,
}

public sealed record PrerequisiteState
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required PrerequisiteStatus Status { get; init; }
    public string? Path { get; init; }
    public string? InstalledVersion { get; init; }
    public string? LatestVersion { get; init; }
    /// <summary>Kept current by <c>agentbox prerequisite update</c>.</summary>
    public bool FollowsLatest { get; init; }
    /// <summary>Installed by agentbox or an AgentBox Feature (so <c>update</c> may touch it).</summary>
    public bool Managed { get; init; }
    public string? Detail { get; init; }
}

public sealed record InspectOptions(bool CheckUpdates = false, bool Deep = false);

/// <summary>
/// Looks at one prerequisite without changing anything. By default only cheap checks: PATH lookup
/// and versions read from files on disk (dotnet tool store, npm package.json, template cache).
/// <see cref="InspectOptions.Deep"/> also runs the tool; <see cref="InspectOptions.CheckUpdates"/>
/// asks nuget.org/npm for the latest version.
/// </summary>
public sealed class PrerequisiteInspector(IHostInfo host, IProcessRunner processes, IPackageRegistryClient registry, StateStore state)
{
    public const string LinuxPaPrefix = "/usr/local/lib/power-apps-cli";

    public async Task<PrerequisiteState> InspectAsync(Prerequisite prerequisite, InspectOptions options, CancellationToken cancellationToken)
    {
        if (prerequisite.ContainerOnly && !(host.Os == OsKind.Linux && host.HostClass == HostClass.Container))
            return Row(prerequisite, PrerequisiteStatus.NotApplicable, detail: "not needed outside containers");

        var installedRecord = state.Load().Installed.GetValueOrDefault(prerequisite.Id);
        var marker = prerequisite.LinuxAutoUpdateMarker is { } m && host.Os == OsKind.Linux && host.FileExists(m);
        var managed = marker || installedRecord is not null || prerequisite.Kind == PrerequisiteKind.DotnetTemplate;
        var followsLatest = prerequisite.Kind switch
        {
            PrerequisiteKind.DotnetTemplate => true,
            PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool => marker || installedRecord?.FollowsLatest == true,
            _ => false,
        };

        string? path;
        string? version;
        if (prerequisite.Kind == PrerequisiteKind.DotnetTemplate)
        {
            version = InstalledTemplateVersion(prerequisite.Package!)?.ToNormalizedString();
            path = version is null ? null : TemplatePackagesDirectory;
        }
        else
        {
            path = processes.FindOnPath(prerequisite.Command);
            version = path is null ? null : InstalledVersion(prerequisite, path)?.ToNormalizedString();
        }

        if (path is null)
            return Row(prerequisite, PrerequisiteStatus.Missing, managed: managed, followsLatest: followsLatest);

        if (options.Deep && prerequisite.Kind != PrerequisiteKind.DotnetTemplate)
        {
            var run = await processes.RunAsync(prerequisite.Command, prerequisite.VersionArguments, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!run.Succeeded)
            {
                var reason = run.TimedOut ? "timed out" : $"exit code {run.ExitCode}";
                return Row(prerequisite, PrerequisiteStatus.Broken, path, version, managed: managed, followsLatest: followsLatest,
                    detail: $"'{prerequisite.Command} {string.Join(' ', prerequisite.VersionArguments)}' failed ({reason})");
            }
        }

        string? latest = null;
        string? detail = null;
        var status = PrerequisiteStatus.Ok;
        if (options.CheckUpdates && followsLatest && prerequisite.Package is not null)
        {
            try
            {
                var latestVersion = await LatestAsync(prerequisite, cancellationToken).ConfigureAwait(false);
                latest = latestVersion?.ToNormalizedString();
                if (latestVersion is not null && NuGetVersion.TryParse(version, out var installed) && latestVersion > installed)
                    status = PrerequisiteStatus.Outdated;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                detail = "update check skipped: " + ex.Message;
            }
        }

        return Row(prerequisite, status, path, version, latest, managed, followsLatest, detail);
    }

    public Task<NuGetVersion?> LatestAsync(Prerequisite prerequisite, CancellationToken cancellationToken) =>
        prerequisite.Kind == PrerequisiteKind.NpmTool
            ? registry.GetLatestNpmVersionAsync(prerequisite.Package!, cancellationToken)
            : registry.GetLatestNuGetVersionAsync(prerequisite.Package!, cancellationToken);

    /// <summary>Cheap installed-version lookup; null when it can't be read from disk.</summary>
    public NuGetVersion? InstalledVersion(Prerequisite prerequisite, string commandPath) => prerequisite.Kind switch
    {
        PrerequisiteKind.DotnetTool => DotnetToolVersion(prerequisite.Package!),
        PrerequisiteKind.NpmTool => NpmPackageVersion(prerequisite.Package!, commandPath),
        PrerequisiteKind.DotnetTemplate => InstalledTemplateVersion(prerequisite.Package!),
        _ => null,
    };

    private string DotnetToolsStore => System.IO.Path.Combine(host.HomeDirectory, ".dotnet", "tools", ".store");

    private string TemplatePackagesDirectory => System.IO.Path.Combine(host.HomeDirectory, ".templateengine", "packages");

    private NuGetVersion? DotnetToolVersion(string packageId)
    {
        var directory = System.IO.Path.Combine(DotnetToolsStore, packageId.ToLowerInvariant());
        if (!host.DirectoryExists(directory))
            return null;
        return System.IO.Directory.GetDirectories(directory)
            .Select(d => NuGetVersion.TryParse(System.IO.Path.GetFileName(d), out var v) ? v : null)
            .Where(v => v is not null)
            .Max();
    }

    private NuGetVersion? InstalledTemplateVersion(string packageId)
    {
        if (!host.DirectoryExists(TemplatePackagesDirectory))
            return null;
        var prefix = packageId + ".";
        return System.IO.Directory.GetFiles(TemplatePackagesDirectory, packageId + ".*.nupkg")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Select(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && NuGetVersion.TryParse(n[prefix.Length..], out var v) ? v : null)
            .Where(v => v is not null)
            .Max();
    }

    private NuGetVersion? NpmPackageVersion(string packageName, string commandPath)
    {
        var candidates = new List<string>();
        if (host.Os == OsKind.Linux)
            candidates.Add(System.IO.Path.Combine(LinuxPaPrefix, "lib", "node_modules", packageName, "package.json"));
        if (host.Os == OsKind.Windows && host.GetEnvironmentVariable("APPDATA") is { } appData)
            candidates.Add(System.IO.Path.Combine(appData, "npm", "node_modules", packageName, "package.json"));

        // npm puts a symlink to the package's bin script on PATH (macOS/Linux): walk up from its target.
        string? resolved;
        try
        {
            resolved = new FileInfo(commandPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            resolved = null; // gone or unreadable: no version, not an error
        }
        for (var dir = resolved is null ? null : System.IO.Path.GetDirectoryName(resolved); dir is not null; dir = System.IO.Path.GetDirectoryName(dir))
            candidates.Add(System.IO.Path.Combine(dir, "package.json"));

        foreach (var candidate in candidates.Where(host.FileExists))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (document.RootElement.TryGetProperty("name", out var name) && name.GetString() == packageName
                    && NuGetVersion.TryParse(document.RootElement.GetProperty("version").GetString(), out var version))
                    return version;
            }
            catch (JsonException)
            {
                // Not a package manifest we can read; try the next candidate.
            }
        }
        return null;
    }

    private static PrerequisiteState Row(
        Prerequisite p, PrerequisiteStatus status, string? path = null, string? version = null, string? latest = null,
        bool managed = false, bool followsLatest = false, string? detail = null) => new()
        {
            Id = p.Id,
            Description = p.Description,
            Status = status,
            Path = path,
            InstalledVersion = version,
            LatestVersion = latest,
            Managed = managed,
            FollowsLatest = followsLatest,
            Detail = detail,
        };
}
