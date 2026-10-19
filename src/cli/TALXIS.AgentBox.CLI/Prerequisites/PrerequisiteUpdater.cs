using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Prerequisites;

public enum UpdateAction { UpToDate, Updated, Failed, Offline, Busy, NotManaged, Missing }

public sealed record UpdateResult(string Id, UpdateAction Action, string? From = null, string? To = null, string? Detail = null);

/// <summary>
/// Brings prerequisites that follow the latest release up to date, in place, when a newer version is
/// published. Runs on every container start, so it is quick when there's nothing to do, never touches
/// pinned tools, treats offline as normal, and lets only one run at a time. Containers only: on a
/// person's own machine it changes nothing.
/// </summary>
public sealed class PrerequisiteUpdater(IHostInfo host, IProcessRunner processes, PrerequisiteInspector inspector, ILogger logger)
{
    // Under the start hook's `timeout 120`, so it finishes rather than being killed mid-write.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(100);

    public async Task<IReadOnlyList<UpdateResult>> UpdateAsync(IReadOnlyList<Prerequisite> targets, CancellationToken cancellationToken)
    {
        if (!host.IsLinuxContainer)
            return targets.Select(t => new UpdateResult(t.Id, UpdateAction.NotManaged, Detail: "agentbox only checks on your own machine")).ToList();

        var lockPath = Path.Combine(host.HomeDirectory, ".agentbox", "update.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return targets.Select(t => new UpdateResult(t.Id, UpdateAction.Busy, Detail: "another update is running")).ToList();
        }

        await using (lockFile.ConfigureAwait(false))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Deadline);
            return await Task.WhenAll(targets.Select(t => UpdateOneAsync(t, deadline.Token))).ConfigureAwait(false);
        }
    }

    private async Task<UpdateResult> UpdateOneAsync(Prerequisite p, CancellationToken cancellationToken)
    {
        if (p.Package is null)
            return new(p.Id, UpdateAction.NotManaged, Detail: p.Kind == PrerequisiteKind.SelfUpdating ? "updates itself" : "comes with the image or OS");

        var current = await inspector.InspectAsync(p, checkUpdates: false, cancellationToken).ConfigureAwait(false);
        if (current.Status == PrerequisiteStatus.Missing)
            return new(p.Id, UpdateAction.Missing);
        if (!current.FollowsLatest)
            return new(p.Id, UpdateAction.NotManaged, current.InstalledVersion, Detail: "pinned, or not installed by AgentBox");

        NuGetVersion? latest;
        try
        {
            latest = await inspector.LatestAsync(p, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(p.Id, UpdateAction.Offline, current.InstalledVersion, Detail: ex.Message);
        }
        if (latest is null || (NuGetVersion.TryParse(current.InstalledVersion, out var installed) && installed >= latest))
            return new(p.Id, UpdateAction.UpToDate, current.InstalledVersion);

        var version = latest.ToNormalizedString();
        var (file, arguments) = p.Kind switch
        {
            PrerequisiteKind.DotnetTool => ("dotnet", new[] { "tool", "update", "--global", p.Package, "--version", version }),
            PrerequisiteKind.DotnetTemplate => ("dotnet", ["new", "install", $"{p.Package}::{version}"]),
            _ => ("npm", NpmInstall(p, version)),
        };
        logger.LogInformation("Updating {Id} {From} -> {To}", p.Id, current.InstalledVersion ?? "?", version);
        try
        {
            var run = await processes.RunAsync(file, arguments, Deadline, cancellationToken: cancellationToken).ConfigureAwait(false);
            return run.Succeeded
                ? new(p.Id, UpdateAction.Updated, current.InstalledVersion, version)
                : new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, run.FailureSummary);
        }
        catch (OperationCanceledException)
        {
            return new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, "ran out of time");
        }
    }

    /// <summary>Keeps a Feature-installed npm tool in the Feature's own prefix.</summary>
    private string[] NpmInstall(Prerequisite p, string version) =>
        p.NpmPrefix is { } prefix && host.DirectoryExists(prefix)
            ? ["install", "--global", "--prefix", prefix, $"{p.Package}@{version}"]
            : ["install", "--global", $"{p.Package}@{version}"];
}
