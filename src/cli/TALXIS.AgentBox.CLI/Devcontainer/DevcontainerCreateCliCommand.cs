using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Devcontainer;
using TALXIS.AgentBox.CLI.Core.Logging;

namespace TALXIS.AgentBox.CLI.Devcontainer;

[CliIdempotent]
[CliCommand(Name = "create", Description = "Write .devcontainer/devcontainer.json pointing at the AgentBox image. The image brings the config for its own tools, so the file never needs changing when AgentBox does; add your project's own settings to it.")]
public class DevcontainerCreateCliCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger(nameof(DevcontainerCreateCliCommand));

    [CliArgument(Description = "Repository directory (default: the current directory).", Required = false)]
    public string Directory { get; set; } = ".";

    [CliOption(Name = "--name", Description = "Name shown by devcontainer tools (default: the directory name).", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--force-overwrite", Description = "Replace an existing devcontainer.json.", Required = false)]
    public bool ForceOverwrite { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var root = Path.GetFullPath(Directory);
        if (!System.IO.Directory.Exists(root))
            throw new ArgumentException($"Directory '{Directory}' does not exist.");
        var path = Path.Combine(root, DevcontainerFile.RelativePath);
        if (File.Exists(path) && !ForceOverwrite)
            throw new ArgumentException($"{path} already exists. Use --force-overwrite to replace it.");

        var name = string.IsNullOrWhiteSpace(Name) ? new DirectoryInfo(root).Name : Name;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DevcontainerFile.Render(name));
        OutputFormatter.WriteResult("created", path);
        return Task.FromResult(ExitSuccess);
    }
}
