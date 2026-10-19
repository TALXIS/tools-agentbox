using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Harness;
using TALXIS.AgentBox.CLI.Core.Logging;

namespace TALXIS.AgentBox.CLI.Harness;

[CliReadOnly]
[CliCommand(Name = "list", Description = "List the known harnesses, whether each is installed here, and whether it's set up.")]
public class HarnessListCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(HarnessListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = AgentBoxServices.Current.Harness(Logger);
        var rows = await Task.WhenAll(Enum.GetValues<HarnessKind>().Select(h => service.InspectAsync(h, CancellationToken.None))).ConfigureAwait(false);
        OutputFormatter.WriteList(rows, items =>
        {
            foreach (var r in items)
            {
                var status = !r.Detected ? "not installed" : r.PluginRegistered ? "set up" : "not set up";
                OutputWriter.WriteLine($"  {HarnessCliCommand.Id(r.Harness),-10} {status,-14} {r.Detail}".TrimEnd());
            }
        });
        return ExitSuccess;
    }
}
