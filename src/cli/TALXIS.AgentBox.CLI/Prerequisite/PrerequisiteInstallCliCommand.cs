using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Logging;
using TALXIS.AgentBox.CLI.Core.Prerequisites;

namespace TALXIS.AgentBox.CLI.Prerequisite;

[CliIdempotent]
[CliCommand(Name = "install", Description = "Install the missing prerequisites (never updates; see 'update'). In a container it installs everything, using sudo when needed; on a workstation it prints the command for those that need an administrator.")]
public class PrerequisiteInstallCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(PrerequisiteInstallCliCommand));

    [CliArgument(Description = "Prerequisite ids to install (default: all).", Required = false)]
    public string[]? Ids { get; set; }

    [CliOption(Name = "--reinstall", Description = "Install even if present (repairs a broken install).", Required = false)]
    public bool Reinstall { get; set; }

    [CliOption(Name = "--strict", Description = "Exit with 1 if anything failed. Without it, failures are reported but the exit code stays 0, so container start hooks never fail.", Required = false)]
    public bool Strict { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var targets = PrerequisiteCliCommand.Select(Ids);
        var results = await PrerequisiteCliCommand.InstallAsync(AgentBoxServices.Current, targets, Reinstall, Logger).ConfigureAwait(false);
        OutputFormatter.WriteList(results, items =>
        {
            foreach (var r in items)
            {
                OutputWriter.WriteLine($"  {Label(r.Action),-9} {r.Id,-17} {r.Detail}".TrimEnd());
                if (r.Command is not null)
                    OutputWriter.WriteLine($"  {"",-9} {"",-17} {r.Command}");
            }
        });

        var failed = results.Count(r => r.Action == InstallAction.Failed);
        if (failed > 0)
            Logger.LogWarning("{Count} prerequisite(s) failed to install.", failed);
        return failed > 0 && Strict ? ExitError : ExitSuccess;
    }

    internal static string Label(InstallAction action) => action switch
    {
        InstallAction.AlreadyPresent => "OK",
        InstallAction.Installed => "INSTALLED",
        InstallAction.Manual => "MANUAL",
        InstallAction.Failed => "FAILED",
        _ => "N/A",
    };
}
