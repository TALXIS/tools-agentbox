using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Cli;

namespace TALXIS.AgentBox.CLI.Harness;

[CliCommand(
    Name = "harness",
    Description = "The coding-agent harnesses (Claude Code, GitHub Copilot): set them up with the TALXIS/skills plugin and check them.",
    Children = new[] { typeof(HarnessListCommand), typeof(HarnessConfigureCommand), typeof(HarnessValidateCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class HarnessCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}

[CliCommand(Name = "list", Description = "List the harnesses, whether each is installed, and whether the plugin is set up.")]
public class HarnessListCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("harness list");

    protected override async Task<int> ExecuteAsync()
    {
        var service = AgentBoxServices.Current.Harness(Logger);
        var rows = await Task.WhenAll(Enum.GetValues<HarnessKind>().Select(service.InspectAsync)).ConfigureAwait(false);
        OutputFormatter.Write(rows, items =>
        {
            foreach (var r in items)
            {
                var status = !r.Installed ? "not installed" : r.PluginEnabled ? $"set up ({r.Skills} skills)" : "not set up";
                OutputWriter.WriteLine($"  {HarnessService.Id(r.Harness),-10} {status}");
            }
        });
        return ExitSuccess;
    }
}

[CliCommand(Name = "configure", Description = "Set up the harnesses with the TALXIS/skills plugin. Without names: every installed harness (on your own machine, you're asked when there's more than one).")]
public class HarnessConfigureCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("harness configure");

    [CliArgument(Description = "Harnesses to set up: claude, copilot.", Required = false)]
    public string[]? Names { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = AgentBoxServices.Current.Harness(Logger);
        var results = new List<ConfigureResult>();
        foreach (var harness in service.Select(Names))
            results.Add(await service.ConfigureAsync(harness).ConfigureAwait(false));
        OutputFormatter.Write(results, items =>
        {
            foreach (var r in items)
                OutputWriter.WriteLine($"  {OutputFormatter.Label(r.Status),-14} {HarnessService.Id(r.Harness),-10} {r.Detail}".TrimEnd());
        });
        return results.Any(r => r.Status == ConfigureStatus.Failed) ? ExitError : ExitSuccess;
    }
}

[CliCommand(Name = "validate", Description = "Check the harnesses are ready: plugin installed and enabled with its skills. --deep also starts its MCP servers. Exits with 3 when one isn't ready.")]
public class HarnessValidateCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("harness validate");

    [CliArgument(Description = "Harnesses to check: claude, copilot (default: the installed ones).", Required = false)]
    public string[]? Names { get; set; }

    [CliOption(Name = "--deep", Description = "Also start each MCP server the plugin declares and check it answers with tools.", Required = false)]
    public bool Deep { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = AgentBoxServices.Current.Harness(Logger);
        var harnesses = Names is { Length: > 0 } ? HarnessService.Parse(Names) : Enum.GetValues<HarnessKind>().Where(service.IsInstalled).ToList();
        // One at a time: both harnesses usually declare the same server, and two first runs of it
        // (dnx downloading the package) would race.
        var results = new List<HarnessHealth>();
        foreach (var harness in harnesses)
            results.Add(await service.ValidateAsync(harness, Deep).ConfigureAwait(false));
        OutputFormatter.Write(results, items =>
        {
            foreach (var r in items)
            {
                OutputWriter.WriteLine($"  {(r.Healthy ? "ok" : "unhealthy"),-10} {HarnessService.Id(r.Harness),-10} {(r.Skills > 0 ? $"{r.Skills} skills" : "")} {r.Detail}".TrimEnd());
                foreach (var mcp in r.McpServers)
                    OutputWriter.WriteLine($"  {"",-10} {"",-10} mcp {mcp.Server}: {(mcp.Answered ? $"{mcp.Tools} tools" : "no answer")} {mcp.Error}".TrimEnd());
            }
        });
        return results.All(r => r.Healthy) ? ExitSuccess : ExitUnhealthy;
    }
}
