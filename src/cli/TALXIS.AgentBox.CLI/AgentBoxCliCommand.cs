using DotMake.CommandLine;

namespace TALXIS.AgentBox.CLI;

[CliCommand(
    Description = "Makes this machine or container ready for coding agents working on Power Platform / Dataverse.",
    Children = new[] { typeof(SetupCommand), typeof(Prerequisites.PrerequisiteCliCommand), typeof(Harness.HarnessCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class AgentBoxCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
