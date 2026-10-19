using DotMake.CommandLine;

namespace TALXIS.AgentBox.CLI.Devcontainer;

[CliCommand(
    Name = "devcontainer",
    Alias = "dc",
    Description = "Add the AgentBox devcontainer config to a repository, for GitHub Codespaces or any devcontainer tool.",
    Children = new[] { typeof(DevcontainerCreateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class DevcontainerCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
