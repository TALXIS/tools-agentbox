using System.Text.Json;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Host;

namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

public sealed record InstalledRecord(string? Version, DateTimeOffset InstalledAt, bool FollowsLatest);

public sealed record AgentBoxState
{
    /// <summary>Prerequisites agentbox itself installed. <c>update</c> only ever touches these
    /// (plus tools whose Feature left an auto-update marker), never a user's own installs.</summary>
    public Dictionary<string, InstalledRecord> Installed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// agentbox's own state: <c>/var/lib/agentbox</c> for root in a container (shared by the machine),
/// otherwise <c>~/.agentbox</c>; <c>AGENTBOX_STATE_DIR</c> overrides both.
/// </summary>
public sealed class StateStore(IHostInfo host)
{
    public string Directory { get; } =
        host.GetEnvironmentVariable("AGENTBOX_STATE_DIR") is { Length: > 0 } overridden ? overridden
        : host.HostClass == HostClass.Container && host.IsRoot && host.Os == OsKind.Linux ? "/var/lib/agentbox"
        : Path.Combine(host.HomeDirectory, ".agentbox");

    public string StatePath => Path.Combine(Directory, "state.json");

    public string LastRunPath => Path.Combine(Directory, "last-run.json");

    public string UpdateLockPath => Path.Combine(Directory, "update.lock");

    public AgentBoxState Load()
    {
        if (!File.Exists(StatePath))
            return new AgentBoxState();
        try
        {
            return JsonSerializer.Deserialize<AgentBoxState>(File.ReadAllText(StatePath), AgentBoxOutputJsonOptions.Default) ?? new AgentBoxState();
        }
        catch (JsonException)
        {
            return new AgentBoxState();
        }
    }

    public void RecordInstalled(string id, string? version, bool followsLatest)
    {
        var state = Load();
        state.Installed[id] = new InstalledRecord(version, DateTimeOffset.UtcNow, followsLatest);
        Write(StatePath, state);
    }

    public void WriteLastRun<T>(T result) => Write(LastRunPath, result);

    private void Write<T>(string path, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(path, JsonSerializer.Serialize(value, AgentBoxOutputJsonOptions.Default));
    }
}
