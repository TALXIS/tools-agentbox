using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Logging;
using TALXIS.AgentBox.CLI.Core.Prerequisites;

namespace TALXIS.AgentBox.CLI.Prerequisite;

[CliIdempotent]
[CliCommand(Name = "update", Description = "Bring the prerequisites that follow the latest release up to date, in place: no restart needed. Leaves pinned and self-updating ones alone, and never fails a container start (offline is fine).")]
public class PrerequisiteUpdateCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(PrerequisiteUpdateCliCommand));

    [CliArgument(Description = "Prerequisite ids to update (default: all).", Required = false)]
    public string[]? Ids { get; set; }

    [CliOption(Name = "--all", Description = "In a container, also reinstall the other prerequisites at their latest versions, using sudo when needed. Takes minutes rather than seconds.", Required = false)]
    public bool All { get; set; }

    [CliOption(Name = "--timeout", Description = "Deadline in seconds for the default updates (default 120). Doesn't limit --all.", Required = false)]
    public int TimeoutSeconds { get; set; } = 120;

    [CliOption(Name = "--strict", Description = "Exit with 1 if an update failed.", Required = false)]
    public bool Strict { get; set; }

    public sealed record UpdateOutput(long DurationMs, IReadOnlyList<UpdateResult> Results);

    protected override async Task<int> ExecuteAsync()
    {
        if (TimeoutSeconds <= 0)
            throw new ArgumentException("--timeout must be a positive number of seconds.");
        var targets = PrerequisiteCliCommand.Select(Ids);
        var services = AgentBoxServices.Current;
        var host = services.Host;
        var inContainer = host.Os == OsKind.Linux && host.HostClass == HostClass.Container;
        if (All && !inContainer)
            Logger.LogWarning("--all only applies in a Linux container; updating the rest as usual.");

        var reinstall = All && inContainer
            ? await ReinstallTargetsAsync(services, targets).ConfigureAwait(false)
            : [];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<UpdateResult>();
        if (reinstall.Count > 0)
        {
            Logger.LogInformation("Reinstalling {Count} prerequisite(s) at their latest versions.", reinstall.Count);
            var installed = await PrerequisiteCliCommand.InstallAsync(services, reinstall, reinstall: true, Logger).ConfigureAwait(false);
            results.AddRange(installed.Select(r => r.Action == InstallAction.Installed
                ? new UpdateResult(r.Id, UpdateAction.Updated, Detail: "reinstalled")
                : new UpdateResult(r.Id, UpdateAction.Failed, Detail: r.Detail)));
        }

        var reinstalled = reinstall.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var run = await services.Updater(Logger).UpdateAsync(targets.Where(t => !reinstalled.Contains(t.Id)).ToList(), TimeSpan.FromSeconds(TimeoutSeconds), CancellationToken.None).ConfigureAwait(false);
        results.AddRange(run.Results);
        var ordered = targets.Select(t => results.First(r => string.Equals(r.Id, t.Id, StringComparison.OrdinalIgnoreCase))).ToList();

        OutputFormatter.WriteData(new UpdateOutput(clock.ElapsedMilliseconds, ordered), output =>
        {
            // The ones update never touches would only be noise here; JSON output keeps them.
            foreach (var x in output.Results.Where(r => r.Action != UpdateAction.NotManaged))
            {
                var versions = x.Action == UpdateAction.Updated && x.To is not null ? $"{x.From ?? "?"} -> {x.To}" : x.From ?? "";
                OutputWriter.WriteLine($"  {Label(x.Action),-9} {x.Id,-17} {versions,-26} {x.Detail}".TrimEnd());
            }
            var skipped = output.Results.Count(r => r.Action == UpdateAction.NotManaged);
            if (skipped > 0)
                OutputWriter.WriteLine($"{skipped} other prerequisite(s) update themselves or with --all.");
            OutputWriter.WriteLine($"Done in {output.DurationMs / 1000.0:0.0} s.");
        });
        return Strict && ordered.Any(x => x.Action == UpdateAction.Failed) ? ExitError : ExitSuccess;
    }

    /// <summary>
    /// For --all: present prerequisites that a Feature installs and that the default update doesn't
    /// cover (it handles those that follow latest; self-updating ones are left alone).
    /// </summary>
    private static async Task<List<Core.Prerequisites.Prerequisite>> ReinstallTargetsAsync(AgentBoxServices services, IReadOnlyList<Core.Prerequisites.Prerequisite> targets)
    {
        var inspector = services.Inspector();
        var result = new List<Core.Prerequisites.Prerequisite>();
        foreach (var target in targets.Where(t => t.Feature is not null && t.Kind == PrerequisiteKind.System))
        {
            var state = await inspector.InspectAsync(target, new InspectOptions(), CancellationToken.None).ConfigureAwait(false);
            if (state.Status is not (PrerequisiteStatus.Missing or PrerequisiteStatus.NotApplicable))
                result.Add(target);
        }
        return result;
    }

    internal static string Label(UpdateAction action) => action switch
    {
        UpdateAction.UpToDate => "OK",
        UpdateAction.Updated => "UPDATED",
        UpdateAction.Failed => "FAILED",
        UpdateAction.UpdateCheckSkipped => "OFFLINE",
        UpdateAction.Missing => "MISSING",
        UpdateAction.Skipped => "BUSY",
        _ => "SKIPPED",
    };
}
