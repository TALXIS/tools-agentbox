namespace TALXIS.AgentBox.CLI.Core.Devcontainer;

/// <summary>
/// The minimal .devcontainer/devcontainer.json for a project. The config for AgentBox's own tools
/// (updates on start, the keyring, plugin registration, editor extensions) comes from the pre-built
/// image's devcontainer.metadata label, so this file never has to change when AgentBox does.
/// Project config (files to open, ports, settings) is the repository's to add.
/// </summary>
public static class DevcontainerFile
{
    public const string Image = "ghcr.io/talxis/tools-agentbox/image:latest";

    public static string RelativePath { get; } = Path.Combine(".devcontainer", "devcontainer.json");

    public static string Render(string name) =>
        "{\n" +
        $"    \"name\": {System.Text.Json.JsonSerializer.Serialize(name)},\n" +
        $"    \"image\": \"{Image}\"\n" +
        "}\n";
}
