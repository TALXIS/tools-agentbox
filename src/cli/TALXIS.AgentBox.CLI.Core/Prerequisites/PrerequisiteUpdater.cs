using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using TALXIS.AgentBox.CLI.Core.Host;

namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

public enum UpdateAction
{
    UpToDate = 0,
    Updated = 1,
    Failed = 2,
    UpdateCheckSkipped = 3,
    NotManaged = 4,
    Missing = 5,
    Skipped = 6,
}

public sealed record UpdateResult(string Id, UpdateAction Action, string? From = null, string? To = null, string? Detail = null, long DurationMs = 0);

public sealed record UpdateRun(DateTimeOffset StartedAt, long DurationMs, IReadOnlyList<UpdateResult> Results);

/// <summary>
/// <c>agentbox prerequisite update</c>, meant for every container start: for each tool that
/// follows latest and that agentbox (or an AgentBox Feature) installed, compare the version on
/// disk with nuget.org/npm and update only when a newer one exists. All checks run in parallel
/// under one deadline, guarded by a machine-wide lock so overlapping starts don't race; offline or
/// registry errors never fail it.
/// </summary>
public sealed class PrerequisiteUpdater(IHostInfo host, IProcessRunner processes, PrerequisiteInspector inspector, StateStore state, ILogger logger)
{
    public async Task<UpdateRun> UpdateAsync(IReadOnlyList<Prerequisite> targets, TimeSpan deadline, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();

        Directory.CreateDirectory(state.Directory);
        FileStream? lockFile;
        try
        {
            lockFile = new FileStream(state.UpdateLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            logger.LogInformation("Another 'agentbox prerequisite update' is running; skipping.");
            return new UpdateRun(started, 0, targets.Select(t => new UpdateResult(t.Id, UpdateAction.Skipped, Detail: "another update is running")).ToList());
        }

        await using (lockFile.ConfigureAwait(false))
        {
            using var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadlineSource.CancelAfter(deadline);
            var results = await Task.WhenAll(targets.Select(t => UpdateOneAsync(t, deadlineSource.Token))).ConfigureAwait(false);
            var run = new UpdateRun(started, clock.ElapsedMilliseconds, results);
            state.WriteLastRun(run);
            return run;
        }
    }

    private async Task<UpdateResult> UpdateOneAsync(Prerequisite p, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (p.Kind is not (PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool or PrerequisiteKind.DotnetTemplate))
            return new(p.Id, UpdateAction.NotManaged, Detail: p.Kind == PrerequisiteKind.SelfUpdating ? "updates itself" : "updated with --all, or with the image or OS");

        var current = await inspector.InspectAsync(p, new InspectOptions(), cancellationToken).ConfigureAwait(false);
        if (current.Status is PrerequisiteStatus.Missing or PrerequisiteStatus.NotApplicable)
            return new(p.Id, UpdateAction.Missing, Detail: current.Status == PrerequisiteStatus.Missing ? "not installed (see 'agentbox prerequisite install')" : current.Detail);
        if (!current.Managed || !current.FollowsLatest)
            return new(p.Id, UpdateAction.NotManaged, current.InstalledVersion, Detail: !current.Managed ? "not installed by agentbox" : "pinned");

        NuGetVersion? latest;
        try
        {
            latest = await inspector.LatestAsync(p, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(p.Id, UpdateAction.UpdateCheckSkipped, current.InstalledVersion, Detail: ex.Message, DurationMs: clock.ElapsedMilliseconds);
        }

        if (latest is null || (NuGetVersion.TryParse(current.InstalledVersion, out var installed) && installed >= latest))
            return new(p.Id, UpdateAction.UpToDate, current.InstalledVersion, latest?.ToNormalizedString(), DurationMs: clock.ElapsedMilliseconds);

        var version = latest.ToNormalizedString();
        var (file, args) = p.Kind switch
        {
            PrerequisiteKind.DotnetTool => ("dotnet", new[] { "tool", "update", "--global", p.Package!, "--version", version }),
            PrerequisiteKind.DotnetTemplate => ("dotnet", ["new", "install", $"{p.Package}::{version}"]),
            _ => ("npm", NpmUpdateArguments(p, version)),
        };
        logger.LogInformation("Updating {Id} {From} -> {To}", p.Id, current.InstalledVersion ?? "?", version);
        try
        {
            var run = await processes.RunAsync(file, args, TimeSpan.FromMinutes(5), cancellationToken: cancellationToken).ConfigureAwait(false);
            return run.Succeeded
                ? new(p.Id, UpdateAction.Updated, current.InstalledVersion, version, DurationMs: clock.ElapsedMilliseconds)
                : new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, run.TimedOut ? "timed out" : FirstLine(run.StandardError) ?? $"exit code {run.ExitCode}", clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new(p.Id, UpdateAction.Failed, current.InstalledVersion, version, "deadline reached", clock.ElapsedMilliseconds);
        }
    }

    /// <summary>On Linux, pa lives in its own prefix behind the keyring wrapper (pa-cli Feature); keep it there.</summary>
    private string[] NpmUpdateArguments(Prerequisite p, string version) =>
        host.Os == OsKind.Linux && host.DirectoryExists(PrerequisiteInspector.LinuxPaPrefix) && p.Id == "pa"
            ? ["install", "--global", "--prefix", PrerequisiteInspector.LinuxPaPrefix, $"{p.Package}@{version}"]
            : ["install", "--global", $"{p.Package}@{version}"];

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
