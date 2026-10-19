using DotMake.CommandLine;
using TALXIS.AgentBox.CLI.Core.Harness;

namespace TALXIS.AgentBox.CLI.Harness;

[CliCommand(
    Name = "harness",
    Description = "Detect the coding-agent harnesses on this machine and set them up for AgentBox (the TALXIS/skills plugin).",
    Children = new[] { typeof(HarnessListCliCommand), typeof(HarnessConfigureCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class HarnessCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();

    internal static string Id(HarnessKind harness) => harness.ToString().ToLowerInvariant();

    /// <summary>Parses harness ids; unknown ones are input errors.</summary>
    internal static IReadOnlyList<HarnessKind> Parse(IEnumerable<string> ids) =>
        ids.Select(id => Enum.TryParse<HarnessKind>(id, ignoreCase: true, out var kind) && Enum.IsDefined(kind)
                ? kind
                : throw new ArgumentException($"Unknown harness '{id}'. Known: {string.Join(", ", Enum.GetValues<HarnessKind>().Select(Id))}."))
            .Distinct()
            .ToList();
}
