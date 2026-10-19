using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Logging;
using TALXIS.AgentBox.CLI.Core.Prerequisites;

namespace TALXIS.AgentBox.CLI.Prerequisite;

[CliReadOnly]
[CliCommand(Name = "validate", Description = "Check the prerequisites on this machine. Exits with 0 when all are present, 3 when any is missing or broken. Outdated ones don't fail the check.")]
public class PrerequisiteValidateCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(PrerequisiteValidateCliCommand));

    [CliArgument(Description = "Prerequisite ids to check (default: all). See 'agentbox prerequisite list'.", Required = false)]
    public string[]? Ids { get; set; }

    [CliOption(Name = "--check-updates", Description = "Also look up the latest published versions and report OUTDATED prerequisites.", Required = false)]
    public bool CheckUpdates { get; set; }

    [CliOption(Name = "--deep", Description = "Also run each prerequisite to catch broken installs. Slower.", Required = false)]
    public bool Deep { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var targets = PrerequisiteCliCommand.Select(Ids);
        var inspector = AgentBoxServices.Current.Inspector();
        var options = new InspectOptions(CheckUpdates, Deep);
        var rows = await Task.WhenAll(targets.Select(t => inspector.InspectAsync(t, options, CancellationToken.None))).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, items =>
        {
            foreach (var r in items)
            {
                var version = r.InstalledVersion is null ? "" : r.InstalledVersion + (r.LatestVersion is null || r.LatestVersion == r.InstalledVersion ? "" : $" (latest {r.LatestVersion})");
                OutputWriter.WriteLine($"  {Label(r.Status),-9} {r.Id,-17} {version,-26} {r.Detail}".TrimEnd());
            }
        });
        return rows.Any(r => r.Status is PrerequisiteStatus.Missing or PrerequisiteStatus.Broken) ? ExitUnhealthy : ExitSuccess;
    }

    internal static string Label(PrerequisiteStatus status) => status switch
    {
        PrerequisiteStatus.Ok => "OK",
        PrerequisiteStatus.Missing => "MISSING",
        PrerequisiteStatus.Outdated => "OUTDATED",
        PrerequisiteStatus.Broken => "BROKEN",
        _ => "N/A",
    };
}
