using System.Text.Json;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using TALXIS.DevKit.AgentBox.CLI.Host;
using TALXIS.DevKit.AgentBox.CLI.Registry;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

public enum PrerequisiteStatus { Ok, Missing, Outdated, NotNeeded }

public sealed record PrerequisiteState(string Id, string Description, PrerequisiteStatus Status)
{
    public string? InstalledVersion { get; init; }
    public string? LatestVersion { get; init; }

    /// <summary>Kept on the latest release by <c>agentbox prerequisite update</c>.</summary>
    public bool FollowsLatest { get; init; }

    public string? Detail { get; init; }
}

/// <summary>
/// Looks at one prerequisite without changing anything. Cheap by default: a PATH lookup and versions
/// read from disk. With <c>checkUpdates</c> it also asks nuget.org or npm for the latest version.
/// </summary>
public sealed class PrerequisiteInspector(IHostInfo host, IProcessRunner processes, IPackageRegistryClient registry)
{
    public async Task<PrerequisiteState> InspectAsync(Prerequisite p, bool checkUpdates, CancellationToken cancellationToken)
    {
        if (p.ContainerOnly && !host.IsLinuxContainer)
            return new(p.Id, p.Description, PrerequisiteStatus.NotNeeded);

        var followsLatest = FollowsLatest(p);
        var missing = new PrerequisiteState(p.Id, p.Description, PrerequisiteStatus.Missing) { FollowsLatest = followsLatest };
        var path = p.Kind == PrerequisiteKind.DotnetTemplate ? null : processes.FindOnPath(p.Command);
        var version = p.Kind == PrerequisiteKind.DotnetTemplate ? TemplateVersion(p.Package!) : path is null ? null : InstalledVersion(p);
        if (version is null && path is null)
            return missing;
        if (p.RequiredVersion is { } required && !await HasRequiredVersionAsync(p, required, cancellationToken).ConfigureAwait(false))
            return missing with { Detail = $"'{p.Command} {string.Join(' ', required.Arguments)}' shows no matching version" };

        var current = new PrerequisiteState(p.Id, p.Description, PrerequisiteStatus.Ok)
        {
            InstalledVersion = version?.ToNormalizedString(),
            FollowsLatest = followsLatest,
        };
        if (!checkUpdates || !followsLatest)
            return current;

        try
        {
            var latest = await LatestAsync(p, cancellationToken).ConfigureAwait(false);
            return current with
            {
                LatestVersion = latest?.ToNormalizedString(),
                Status = latest is not null && version is not null && latest > version ? PrerequisiteStatus.Outdated : PrerequisiteStatus.Ok,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return current with { Detail = "couldn't check for updates: " + ex.Message };
        }
    }

    /// <summary>Templates always follow latest; tools do when their Feature said so.</summary>
    private bool FollowsLatest(Prerequisite p) =>
        p.Kind == PrerequisiteKind.DotnetTemplate
        || (p.Package is not null && p.FollowsLatestMarker is { } marker && host.FileExists(marker));

    public Task<NuGetVersion?> LatestAsync(Prerequisite p, CancellationToken cancellationToken) =>
        p.Kind == PrerequisiteKind.NpmTool
            ? registry.GetLatestNpmVersionAsync(p.Package!, cancellationToken)
            : registry.GetLatestNuGetVersionAsync(p.Package!, cancellationToken);

    private async Task<bool> HasRequiredVersionAsync(Prerequisite p, RequiredVersion required, CancellationToken cancellationToken)
    {
        var run = await processes.RunAsync(p.Command, required.Arguments, TimeSpan.FromSeconds(30), cancellationToken: cancellationToken).ConfigureAwait(false);
        return run.Succeeded && Regex.IsMatch(run.StandardOutput, required.OutputPattern, RegexOptions.Multiline);
    }

    private NuGetVersion? InstalledVersion(Prerequisite p) => p.Kind switch
    {
        PrerequisiteKind.DotnetTool => DotnetToolVersion(p),
        PrerequisiteKind.NpmTool => NpmPackageVersion(p),
        _ => null,
    };

    /// <summary>A tool installed for every user (the shared tool path or npm prefix), which only root changes.</summary>
    public bool IsShared(Prerequisite p) => p.Kind switch
    {
        PrerequisiteKind.DotnetTool => p.ToolPath is { } toolPath && host.DirectoryExists(toolPath),
        PrerequisiteKind.NpmTool => p.NpmPrefix is { } prefix && host.DirectoryExists(prefix),
        _ => false,
    };

    private NuGetVersion? DotnetToolVersion(Prerequisite p)
    {
        var tools = IsShared(p) ? p.ToolPath! : Path.Combine(host.HomeDirectory, ".dotnet", "tools");
        return HighestVersionIn(Path.Combine(tools, ".store", p.Package!.ToLowerInvariant()), Path.GetFileName);
    }

    private NuGetVersion? TemplateVersion(string packageId)
    {
        var prefix = packageId + ".";
        return HighestVersionIn(Path.Combine(host.HomeDirectory, ".templateengine", "packages"),
            file => Path.GetFileNameWithoutExtension(file) is var name && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : null);
    }

    private NuGetVersion? HighestVersionIn(string directory, Func<string, string?> versionOf) =>
        host.DirectoryExists(directory)
            ? Directory.EnumerateFileSystemEntries(directory)
                .Select(entry => NuGetVersion.TryParse(versionOf(entry), out var v) ? v : null)
                .Max()
            : null;

    /// <summary>Reads the package's package.json in the Feature's own prefix.</summary>
    private NuGetVersion? NpmPackageVersion(Prerequisite p)
    {
        if (p.NpmPrefix is null)
            return null;
        var manifest = Path.Combine(p.NpmPrefix, "lib", "node_modules", p.Package!, "package.json");
        if (!host.FileExists(manifest))
            return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            return NuGetVersion.TryParse(json.RootElement.GetProperty("version").GetString(), out var version) ? version : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            return null;
        }
    }
}
