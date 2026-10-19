using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core.Host;

namespace TALXIS.AgentBox.CLI.Core.Harness;

public enum HarnessKind
{
    Claude = 0,
    Copilot = 1,
}

public sealed record HarnessState(HarnessKind Harness, bool Detected, string? Path, bool PluginRegistered, string? Detail = null);

public sealed record HarnessConfigureResult(HarnessKind Harness, string Status, string? Detail = null);

/// <summary>
/// Detects coding-agent harnesses and registers the TALXIS/skills plugin (Skills + the txc MCP
/// server) with them. Port of src/container/scripts/configure-agent-harness.sh: same commands,
/// 30 s timeouts, and failures are warnings, never errors.
/// </summary>
public sealed class HarnessService(IHostInfo host, IProcessRunner processes, ILogger logger)
{
    public const string Marketplace = "TALXIS/skills";
    public const string MarketplaceName = "talxis";
    public const string Plugin = "implement@talxis";
    private const string ClaudeSeedDirectory = "/usr/local/claude-plugin-seed";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static string CommandFor(HarnessKind harness) => harness == HarnessKind.Claude ? "claude" : "copilot";

    public async Task<HarnessState> InspectAsync(HarnessKind harness, CancellationToken cancellationToken)
    {
        var path = processes.FindOnPath(CommandFor(harness));
        if (path is null)
            return new(harness, Detected: false, Path: null, PluginRegistered: false);

        var listArguments = harness == HarnessKind.Claude ? new[] { "plugin", "list", "--json" } : ["plugin", "list"];
        var run = await processes.RunAsync(CommandFor(harness), listArguments, Timeout, Environment(harness), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!run.Succeeded)
            return new(harness, true, path, false, "could not list plugins: " + (run.TimedOut ? "timed out" : $"exit code {run.ExitCode}"));

        var registered = harness == HarnessKind.Claude
            ? ClaudeListHasPlugin(run.StandardOutput)
            : run.StandardOutput.Contains("implement", StringComparison.OrdinalIgnoreCase) && run.StandardOutput.Contains(MarketplaceName, StringComparison.OrdinalIgnoreCase);
        return new(harness, true, path, registered);
    }

    public async Task<HarnessConfigureResult> ConfigureAsync(HarnessKind harness, CancellationToken cancellationToken)
    {
        if (processes.FindOnPath(CommandFor(harness)) is null)
            return new(harness, "not-detected");

        var command = CommandFor(harness);
        var environment = Environment(harness);
        var add = await processes.RunAsync(command, ["plugin", "marketplace", "add", Marketplace], Timeout, environment, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!add.Succeeded)
            logger.LogWarning("Could not add the {Marketplace} plugin marketplace to {Harness} ({Detail}); continuing.", Marketplace, harness, Describe(add));

        var installArguments = harness == HarnessKind.Claude
            ? new[] { "plugin", "install", Plugin, "--yes" }
            : ["plugin", "install", Plugin];
        var install = await processes.RunAsync(command, installArguments, Timeout, environment, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!install.Succeeded)
        {
            logger.LogWarning("Could not install {Plugin} into {Harness} ({Detail}).", Plugin, harness, Describe(install));
            return new(harness, "failed", Describe(install));
        }

        if (harness == HarnessKind.Copilot)
            DeclareCopilotPlugin();
        return new(harness, "configured");
    }

    /// <summary>
    /// Claude Code cloud images pre-seed plugins into a machine-wide cache (the environment sets
    /// CLAUDE_CODE_PLUGIN_SEED_DIR); keep writing there when running as root in a container, as the
    /// script did. Elsewhere use the user's own plugin directory.
    /// </summary>
    private Dictionary<string, string?>? Environment(HarnessKind harness) =>
        harness == HarnessKind.Claude && host.Os == OsKind.Linux && host.HostClass == HostClass.Container && host.IsRoot
            ? new() { ["CLAUDE_CODE_PLUGIN_CACHE_DIR"] = ClaudeSeedDirectory }
            : null;

    /// <summary>Declarative form too, so `copilot plugin update` finds it, and new users from /etc/skel start with it.</summary>
    private void DeclareCopilotPlugin()
    {
        var files = new List<string> { System.IO.Path.Combine(host.HomeDirectory, ".copilot", "settings.json") };
        if (host.Os == OsKind.Linux && host.IsRoot)
            files.Add("/etc/skel/.copilot/settings.json");
        foreach (var file in files)
        {
            try
            {
                var settings = File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject existing ? existing : new JsonObject();
                var marketplaces = settings["extraKnownMarketplaces"] as JsonObject ?? new JsonObject();
                marketplaces[MarketplaceName] = new JsonObject { ["source"] = new JsonObject { ["source"] = "github", ["repo"] = Marketplace } };
                settings["extraKnownMarketplaces"] = marketplaces;
                var enabled = settings["enabledPlugins"] as JsonObject ?? new JsonObject();
                enabled[Plugin] = true;
                settings["enabledPlugins"] = enabled;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                File.WriteAllText(file, settings.ToJsonString(Cli.AgentBoxOutputJsonOptions.Default));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("Could not update {File}: {Error}", file, ex.Message);
            }
        }
    }

    internal static bool ClaudeListHasPlugin(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.EnumerateArray().Any(p => p.TryGetProperty("id", out var id) && id.GetString() == Plugin);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Describe(ProcessResult run) =>
        run.TimedOut ? "timed out" : run.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? $"exit code {run.ExitCode}";
}
