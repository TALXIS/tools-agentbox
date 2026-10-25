using System.Text.Json;
using System.Text.Json.Nodes;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Harness;

public enum HarnessKind { Claude, Copilot }

/// <summary>The AgentBox plugin as a harness reports it. <see cref="Directory"/> holds its skills and MCP config.</summary>
public sealed record PluginState(bool Enabled, string? Directory, string? Version = null);

/// <summary>What differs between harnesses: their commands and where they keep the plugin.</summary>
public interface IHarness
{
    HarnessKind Kind { get; }

    string Command { get; }

    /// <summary>Commands that register the TALXIS/skills plugin; each is safe to repeat.</summary>
    IReadOnlyList<string[]> ConfigureSteps { get; }

    /// <summary>The command that brings the installed plugin to the latest version of its marketplace.</summary>
    string[] UpdateStep { get; }

    IReadOnlyDictionary<string, string?>? Environment(IHostInfo host);

    /// <summary>The plugin's state from <c>&lt;command&gt; plugin list --json</c>, or null when it isn't installed.</summary>
    PluginState? ParsePluginList(string json, IHostInfo host);

    /// <summary>Anything to do after the commands, e.g. persisting settings.</summary>
    void AfterConfigure(IHostInfo host) { }
}

public static class Harnesses
{
    public const string Marketplace = "TALXIS/skills";
    public const string Plugin = "implement@talxis";

    public static IReadOnlyList<IHarness> All { get; } = [new ClaudeHarness(), new CopilotHarness()];

    public static IHarness Get(HarnessKind kind) => All.Single(h => h.Kind == kind);

    internal static JsonElement? FindPlugin(string json, Func<JsonElement, bool> isAgentBoxPlugin)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(isAgentBoxPlugin).Select(p => (JsonElement?)p.Clone()).FirstOrDefault()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static bool Flag(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class ClaudeHarness : IHarness
{
    public HarnessKind Kind => HarnessKind.Claude;

    public string Command => "claude";

    public IReadOnlyList<string[]> ConfigureSteps { get; } =
    [
        ["plugin", "marketplace", "add", Harnesses.Marketplace],
        ["plugin", "install", Harnesses.Plugin, "--yes"],
    ];

    public string[] UpdateStep { get; } = ["plugin", "update", Harnesses.Plugin];

    /// <summary>Where the environment pre-seeds plugins (Claude Code cloud sets
    /// CLAUDE_CODE_PLUGIN_SEED_DIR), install into the seed; elsewhere into the user's own directory.</summary>
    public IReadOnlyDictionary<string, string?>? Environment(IHostInfo host) =>
        SeedDirectory(host) is { } seed ? new Dictionary<string, string?> { ["CLAUDE_CODE_PLUGIN_CACHE_DIR"] = seed } : null;

    public PluginState? ParsePluginList(string json, IHostInfo host) =>
        Harnesses.FindPlugin(json, p => p.Text("id") == Harnesses.Plugin) is { } plugin
            ? new(plugin.Flag("enabled"), ResolveInstallPath(plugin.Text("installPath"), host), plugin.Text("version"))
            : null;

    private static string? SeedDirectory(IHostInfo host) =>
        host.GetEnvironmentVariable("CLAUDE_CODE_PLUGIN_SEED_DIR") is { Length: > 0 } seed ? seed.Split(':')[0] : null;

    /// <summary>Claude reports the path under ~/.claude/plugins even when the files live in the seed.</summary>
    private static string? ResolveInstallPath(string? path, IHostInfo host)
    {
        if (path is null || host.DirectoryExists(path) || SeedDirectory(host) is not { } seed)
            return path;
        var userPlugins = Path.Combine(host.HomeDirectory, ".claude", "plugins") + Path.DirectorySeparatorChar;
        return path.StartsWith(userPlugins, StringComparison.Ordinal) ? Path.Combine(seed, path[userPlugins.Length..]) : path;
    }
}

public sealed class CopilotHarness : IHarness
{
    public HarnessKind Kind => HarnessKind.Copilot;

    public string Command => "copilot";

    public IReadOnlyList<string[]> ConfigureSteps { get; } =
    [
        ["plugin", "marketplace", "add", Harnesses.Marketplace],
        ["plugin", "install", Harnesses.Plugin],
    ];

    public string[] UpdateStep { get; } = ["plugin", "update", Harnesses.Plugin];

    public IReadOnlyDictionary<string, string?>? Environment(IHostInfo host) => null;

    public PluginState? ParsePluginList(string json, IHostInfo host) =>
        Harnesses.FindPlugin(json, p => p.Text("name") == "implement" && p.Text("marketplace") == "talxis") is { } plugin
            ? new(plugin.Flag("enabled"), Path.Combine(Home(host), "installed-plugins", "talxis", "implement"), plugin.Text("version"))
            : null;

    /// <summary>Copilot's own directory: <c>COPILOT_HOME</c>, else <c>~/.copilot</c>.</summary>
    private static string Home(IHostInfo host) =>
        host.GetEnvironmentVariable("COPILOT_HOME") is { Length: > 0 } home ? home : Path.Combine(host.HomeDirectory, ".copilot");

    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Also declare it in settings, so `copilot plugin update` keeps it and users created
    /// later from /etc/skel start with it.</summary>
    public void AfterConfigure(IHostInfo host)
    {
        var files = new List<string> { Path.Combine(Home(host), "settings.json") };
        if (host.IsLinuxContainer && host.IsRoot)
            files.Add("/etc/skel/.copilot/settings.json");
        foreach (var file in files)
        {
            // Copilot reads settings.json as JSONC; its comments don't survive the rewrite. Empty counts as {}.
            var text = host.FileExists(file) ? File.ReadAllText(file) : "";
            var settings = !string.IsNullOrWhiteSpace(text) && JsonNode.Parse(text, documentOptions: Jsonc) is JsonObject existing ? existing : [];
            var marketplaces = settings["extraKnownMarketplaces"] as JsonObject ?? [];
            marketplaces["talxis"] = new JsonObject { ["source"] = new JsonObject { ["source"] = "github", ["repo"] = Harnesses.Marketplace } };
            settings["extraKnownMarketplaces"] = marketplaces;
            var enabled = settings["enabledPlugins"] as JsonObject ?? [];
            enabled[Harnesses.Plugin] = true;
            settings["enabledPlugins"] = enabled;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Whole or not at all: Copilot, or another configure, may read it at any moment.
            var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, settings.ToJsonString(AgentBoxOutputJsonOptions.Default));
            File.Move(temporary, file, overwrite: true);
        }
    }
}
