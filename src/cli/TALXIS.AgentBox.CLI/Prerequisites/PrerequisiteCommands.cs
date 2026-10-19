using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Prerequisites;

[CliCommand(
    Name = "prerequisite",
    Description = "The tools AgentBox provides: list, check, install and update them.",
    Children = new[] { typeof(PrerequisiteListCommand), typeof(PrerequisiteValidateCommand), typeof(PrerequisiteInstallCommand), typeof(PrerequisiteUpdateCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PrerequisiteCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}

[CliCommand(Name = "list", Description = "List the prerequisites for this machine, and whether 'install' sets each up here or shows the command to run.")]
public class PrerequisiteListCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("prerequisite list");

    public sealed record Row(string Id, string Description, bool InstalledByAgentBox, string? ManualInstall);

    protected override Task<int> ExecuteAsync()
    {
        var host = AgentBoxServices.Current.Host;
        var rows = PrerequisiteCatalog.All
            .Where(p => !p.ContainerOnly || host.IsLinuxContainer)
            .Select(p => InstalledByAgentBox(host, p) ? new Row(p.Id, p.Description, true, null) : new Row(p.Id, p.Description, false, p.Manual.For(host.Os)))
            .ToList();
        OutputFormatter.Write(rows, items =>
        {
            foreach (var r in items)
                OutputWriter.WriteLine($"  {r.Id,-17} {(r.InstalledByAgentBox ? "install" : "manual"),-8} {r.Description}");
        });
        return Task.FromResult(ExitSuccess);
    }

    internal static bool InstalledByAgentBox(IHostInfo host, Prerequisite p) =>
        host.IsLinuxContainer && (p.Feature is not null || p.Kind == PrerequisiteKind.DotnetTemplate);
}

[CliCommand(Name = "validate", Description = "Check the prerequisites. Exits with 3 when any is missing; outdated ones don't fail the check.")]
public class PrerequisiteValidateCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("prerequisite validate");

    [CliArgument(Description = "Prerequisite ids (default: all).", Required = false)]
    public string[]? Ids { get; set; }

    [CliOption(Name = "--check-updates", Description = "Also look up the latest versions and report outdated prerequisites.", Required = false)]
    public bool CheckUpdates { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var inspector = AgentBoxServices.Current.Inspector();
        var rows = await Task.WhenAll(PrerequisiteCatalog.Select(Ids).Select(p => inspector.InspectAsync(p, CheckUpdates, CancellationToken.None))).ConfigureAwait(false);
        OutputFormatter.Write(rows, Render);
        return rows.Any(r => r.Status == PrerequisiteStatus.Missing) ? ExitUnhealthy : ExitSuccess;
    }

    internal static void Render(IEnumerable<PrerequisiteState> rows)
    {
        foreach (var r in rows)
        {
            var version = r.LatestVersion is { } latest && latest != r.InstalledVersion ? $"{r.InstalledVersion} (latest {latest})" : r.InstalledVersion;
            OutputWriter.WriteLine($"  {OutputFormatter.Label(r.Status),-11} {r.Id,-17} {version,-26} {r.Detail}".TrimEnd());
        }
    }
}

[CliCommand(Name = "install", Description = "Make the prerequisites present. In a container this applies the full AgentBox tool set (as root, through sudo if needed); on your own machine it changes nothing and shows the command for what's missing.")]
public class PrerequisiteInstallCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("prerequisite install");

    [CliArgument(Description = "Prerequisite ids (default: all).", Required = false)]
    public string[]? Ids { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var results = await AgentBoxServices.Current.Installer(Logger).InstallAsync(PrerequisiteCatalog.Select(Ids), CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.Write(results, Render);
        return results.Any(r => r.Action == InstallAction.Failed) ? ExitError : ExitSuccess;
    }

    internal static void Render(IEnumerable<InstallResult> results)
    {
        foreach (var r in results)
            OutputWriter.WriteLine($"  {OutputFormatter.Label(r.Action),-11} {r.Id,-17} {r.Detail}".TrimEnd());
    }
}

[CliCommand(Name = "update", Description = "Bring the prerequisites that follow the latest release up to date, in place, in a container. Pinned and self-updating tools are left alone; offline is not an error.")]
public class PrerequisiteUpdateCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("prerequisite update");

    [CliArgument(Description = "Prerequisite ids (default: all).", Required = false)]
    public string[]? Ids { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var results = await AgentBoxServices.Current.Updater(Logger).UpdateAsync(PrerequisiteCatalog.Select(Ids), CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.Write(results, items =>
        {
            foreach (var r in items.Where(r => r.Action != UpdateAction.NotManaged))
            {
                var versions = r.Action == UpdateAction.Updated ? $"{r.From ?? "?"} -> {r.To}" : r.From;
                OutputWriter.WriteLine($"  {OutputFormatter.Label(r.Action),-11} {r.Id,-17} {versions,-26} {r.Detail}".TrimEnd());
            }
        });
        return results.Any(r => r.Action == UpdateAction.Failed) ? ExitError : ExitSuccess;
    }
}
