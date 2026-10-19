using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Harness;
using TALXIS.AgentBox.CLI.Core.Logging;

namespace TALXIS.AgentBox.CLI.Harness;

[CliIdempotent]
[CliCommand(Name = "configure", Description = "Set up the harnesses for AgentBox. Without ids: the one named by AGENTBOX_HARNESS ('none' skips all), otherwise every installed harness. Failures are warnings unless --strict.")]
public class HarnessConfigureCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(HarnessConfigureCliCommand));

    [CliArgument(Description = "Harness ids to set up (see 'agentbox harness list').", Required = false)]
    public string[]? Ids { get; set; }

    [CliOption(Name = "--strict", Description = "Exit with 1 if a harness couldn't be set up.", Required = false)]
    public bool Strict { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var services = AgentBoxServices.Current;
        IReadOnlyList<HarnessKind> targets;
        if (Ids is { Length: > 0 })
            targets = HarnessCliCommand.Parse(Ids);
        else if (services.Host.GetEnvironmentVariable("AGENTBOX_HARNESS") is { Length: > 0 } fromEnvironment)
            targets = string.Equals(fromEnvironment, "none", StringComparison.OrdinalIgnoreCase) ? [] : HarnessCliCommand.Parse([fromEnvironment]);
        else
            targets = Enum.GetValues<HarnessKind>();

        var service = services.Harness(Logger);
        var results = new List<HarnessConfigureResult>();
        foreach (var harness in targets)
            results.Add(await service.ConfigureAsync(harness, CancellationToken.None).ConfigureAwait(false));

        OutputFormatter.WriteList(results, items =>
        {
            if (items.Count == 0)
                OutputWriter.WriteLine("Nothing to set up (AGENTBOX_HARNESS=none).");
            foreach (var r in items)
                OutputWriter.WriteLine($"  {r.Status.ToUpperInvariant(),-14} {HarnessCliCommand.Id(r.Harness),-10} {r.Detail}".TrimEnd());
        });
        return Strict && results.Any(r => r.Status == "failed") ? ExitError : ExitSuccess;
    }
}
