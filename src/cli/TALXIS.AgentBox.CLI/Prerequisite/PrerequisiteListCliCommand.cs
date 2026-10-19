using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Logging;
using TALXIS.AgentBox.CLI.Core.Prerequisites;

namespace TALXIS.AgentBox.CLI.Prerequisite;

[CliReadOnly]
[CliCommand(Name = "list", Description = "List the prerequisites for this machine, and whether 'install' sets each up here or prints the command to run.")]
public class PrerequisiteListCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(PrerequisiteListCliCommand));

    public sealed record Row(string Id, string Description, bool InstalledByAgentBox, string? ManualCommand);

    protected override Task<int> ExecuteAsync()
    {
        var services = AgentBoxServices.Current;
        var host = services.Host;
        var installer = services.Installer(Logger);
        var rows = PrerequisiteCatalog.All
            .Where(p => !p.ContainerOnly || (host.Os == OsKind.Linux && host.HostClass == HostClass.Container))
            .Select(p =>
            {
                var automatic = InstalledByAgentBox(host, p);
                return new Row(p.Id, p.Description, automatic, automatic ? null : installer.ManualCommand(p));
            })
            .ToList();
        OutputFormatter.WriteList(rows, items =>
        {
            OutputWriter.WriteLine($"Host: {PrerequisiteCliCommand.HostDescription(host)}");
            foreach (var r in items)
                OutputWriter.WriteLine($"  {r.Id,-17} {(r.InstalledByAgentBox ? "install" : "manual"),-8} {r.Description}");
        });
        return Task.FromResult(ExitSuccess);
    }

    /// <summary>Whether <c>prerequisite install</c> installs it on this host, rather than printing a command.</summary>
    internal static bool InstalledByAgentBox(IHostInfo host, Core.Prerequisites.Prerequisite p) =>
        (host.Os == OsKind.Linux && host.HostClass == HostClass.Container && p.Feature is not null)
        || p.Kind is PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool or PrerequisiteKind.DotnetTemplate;
}
