using DotMake.CommandLine;

namespace TALXIS.AgentBox.CLI;

[CliCommand(
    Description = "Set up and check the AgentBox environment for Power Platform / Dataverse development: prerequisites, coding-agent harnesses, devcontainer config.",
    Children = new[] { typeof(Prerequisite.PrerequisiteCliCommand), typeof(Harness.HarnessCliCommand), typeof(Devcontainer.DevcontainerCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class AgentBoxCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
