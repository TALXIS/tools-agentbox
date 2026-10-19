using System.Text.Json;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Prerequisites;

/// <summary>
/// Which prerequisites agentbox installed itself, so <c>update</c> touches those and never someone's
/// own installs. Machine-wide (<c>/var/lib/agentbox</c>) for root in a container, else per user.
/// </summary>
public sealed class StateStore(IHostInfo host)
{
    public string Directory { get; } = host.IsLinuxContainer && host.IsRoot
        ? "/var/lib/agentbox"
        : Path.Combine(host.HomeDirectory, ".agentbox");

    private string StatePath => Path.Combine(Directory, "state.json");

    public string UpdateLockPath => Path.Combine(Directory, "update.lock");

    public bool WasInstalledByAgentBox(string id) => Load().Installed.Contains(id);

    public void RecordInstalled(string id)
    {
        var state = Load();
        if (!state.Installed.Add(id))
            return;
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, AgentBoxOutputJsonOptions.Default));
    }

    private State Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), AgentBoxOutputJsonOptions.Default) ?? new State()
                : new State();
        }
        catch (JsonException)
        {
            return new State();
        }
    }

    private sealed record State
    {
        public HashSet<string> Installed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
