using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

public enum UpdateAction { UpToDate, Updated, Installed, Failed, Offline, Busy, NotManaged, Missing }

public sealed record UpdateResult(string Id, UpdateAction Action, string? From = null, string? To = null, string? Detail = null);

/// <summary>
/// Brings prerequisites that follow the latest release up to date, in place, when a newer version is
/// published. Runs on every container start, so it is quick when there's nothing to do, never touches
/// pinned tools, treats offline as normal, and lets only one run at a time. Tools installed for every
/// user are updated as root (through sudo when needed); templates belong to each user, so a user who
/// lacks them gets them. Containers only: on a person's own machine it changes nothing.
/// </summary>
public sealed class PrerequisiteUpdater(IHostInfo host, IProcessRunner processes, PrerequisiteInspector inspector, ILogger logger)
{
    // Under the start hook's `timeout 120`, so it finishes rather than being killed mid-write.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(100);
    private const string WritableCopy = """rm -rf "$0.agentbox" && cp -a "$0" "$0.agentbox" && rm -rf "$0" && mv "$0.agentbox" "$0" """;

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
            // Only what may need an update: a pinned tool needs no root to be left alone.
            var asRoot = host.IsRoot ? [] : targets.Where(t => inspector.IsShared(t) && inspector.FollowsLatest(t)).ToList();
            var here = Task.WhenAll(targets.Except(asRoot).Select(t => UpdateOneAsync(t, deadline.Token)));
            var elevated = asRoot.Count > 0 ? await UpdateAsRootAsync(asRoot, deadline.Token).ConfigureAwait(false) : [];
            var results = (await here.ConfigureAwait(false)).Concat(elevated).ToList();
            return targets.Select(t => results.First(r => r.Id == t.Id)).ToList();
        }
    }

    private async Task<IReadOnlyList<UpdateResult>> UpdateAsRootAsync(IReadOnlyList<Prerequisite> targets, CancellationToken cancellationToken)
    {
        if (!await Sudo.IsAvailableAsync(processes).ConfigureAwait(false))
            return targets.Select(t => new UpdateResult(t.Id, UpdateAction.Failed, Detail: "installed for every user: needs root, and passwordless sudo isn't available")).ToList();

        var run = await Sudo.RunAgentBoxAsync(processes, ["prerequisite", "update", .. targets.Select(t => t.Id), "--format", "json"],
            Deadline, cancellationToken).ConfigureAwait(false);
        List<UpdateResult> elevated;
        try
        {
            elevated = JsonSerializer.Deserialize<List<UpdateResult>>(run.StandardOutput, AgentBoxOutputJsonOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            elevated = [];
        }
        var missing = $"the update as root reported nothing ({run.FailureSummary})";
        return targets.Select(t => elevated.FirstOrDefault(r => r.Id == t.Id) ?? new UpdateResult(t.Id, UpdateAction.Failed, Detail: missing)).ToList();
    }

    private async Task<UpdateResult> UpdateOneAsync(Prerequisite p, CancellationToken cancellationToken)
    {
        if (p.Package is null)
            return new(p.Id, UpdateAction.NotManaged, Detail: p.Kind switch
            {
                PrerequisiteKind.SelfManaged => "kept current elsewhere",
                PrerequisiteKind.PlaywrightBrowser => "comes with playwright-cli",
                _ => "comes with the image or OS",
            });

        var current = await inspector.InspectAsync(p, checkUpdates: false, cancellationToken).ConfigureAwait(false);
        if (current.Status == PrerequisiteStatus.Missing && p.Kind != PrerequisiteKind.DotnetTemplate)
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
            PrerequisiteKind.DotnetTool when inspector.IsShared(p) => ("dotnet", new[] { "tool", "update", p.Package, "--tool-path", p.ToolPath!, "--version", version }),
            PrerequisiteKind.DotnetTool => ("dotnet", ["tool", "update", "--global", p.Package, "--version", version]),
            PrerequisiteKind.DotnetTemplate => ("dotnet", ["new", "install", $"{p.Package}::{version}"]),
            _ => ("npm", NpmInstall(p, version)),
        };
        var installing = current.Status == PrerequisiteStatus.Missing;
        logger.LogInformation(installing ? "Installing {Id} {To}" : "Updating {Id} {From} -> {To}", p.Id, current.InstalledVersion ?? "?", version);
        try
        {
            // Docker's overlay can't rename a directory from an image layer, which dotnet tool update
            // does to the old version ("Invalid cross-device link", dotnet/sdk#3838). So the update
            // gets a copy in the container's own layer first.
            if (p.Kind == PrerequisiteKind.DotnetTool && inspector.IsShared(p))
            {
                var copy = await processes.RunAsync("bash", ["-c", WritableCopy, p.ToolPath!], Deadline, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!copy.Succeeded)
                    return new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, copy.FailureSummary);
            }
            var run = await processes.RunAsync(file, arguments, Deadline, cancellationToken: cancellationToken).ConfigureAwait(false);
            return run.Succeeded
                ? new(p.Id, installing ? UpdateAction.Installed : UpdateAction.Updated, current.InstalledVersion, version)
                : new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, run.FailureSummary);
        }
        catch (OperationCanceledException)
        {
            return new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, "ran out of time");
        }
    }

    /// <summary>Keeps a Feature-installed npm tool in the Feature's own prefix.</summary>
    private string[] NpmInstall(Prerequisite p, string version) =>
        inspector.IsShared(p)
            ? ["install", "--global", "--prefix", p.NpmPrefix!, $"{p.Package}@{version}"]
            : ["install", "--global", $"{p.Package}@{version}"];
}
