using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Harness;

public sealed record HarnessState(HarnessKind Harness, bool Installed, bool PluginEnabled, int Skills);

public enum ConfigureStatus { Configured, NotInstalled, Failed }

public sealed record ConfigureResult(HarnessKind Harness, ConfigureStatus Status, string? Detail = null);

public sealed record HarnessHealth(HarnessKind Harness, bool Healthy, int Skills, IReadOnlyList<McpProbeResult> McpServers, string? Detail = null);

/// <summary>Detects coding-agent harnesses, registers the TALXIS/skills plugin with them, and checks the result.</summary>
public sealed class HarnessService(IHostInfo host, IProcessRunner processes, IPrompt prompt, ILogger logger)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    public bool IsInstalled(HarnessKind kind) => processes.FindOnPath(Harnesses.Get(kind).Command) is not null;

    /// <summary>
    /// The harnesses to act on: the ones named; else every installed one, except that a person at
    /// their own machine with more than one installed is asked which.
    /// </summary>
    public IReadOnlyList<HarnessKind> Select(IReadOnlyCollection<string>? names)
    {
        if (names is { Count: > 0 })
            return Parse(names);
        var installed = Enum.GetValues<HarnessKind>().Where(IsInstalled).ToList();
        if (installed.Count < 2 || host.IsContainer || !host.IsInteractive)
            return installed;
        var answer = prompt.Ask($"Set up AgentBox for which harness? {string.Join(", ", installed.Select(Id))} or all [all]:");
        return string.IsNullOrWhiteSpace(answer) || answer.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? installed
            : Parse(answer.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<HarnessState> InspectAsync(HarnessKind kind)
    {
        if (!IsInstalled(kind))
            return new(kind, false, false, 0);
        var plugin = await PluginAsync(kind).ConfigureAwait(false);
        return new(kind, true, plugin?.Enabled == true, SkillCount(plugin));
    }

    public async Task<ConfigureResult> ConfigureAsync(HarnessKind kind)
    {
        if (!IsInstalled(kind))
            return new(kind, ConfigureStatus.NotInstalled);
        var harness = Harnesses.Get(kind);
        foreach (var step in harness.ConfigureSteps)
        {
            var run = await processes.RunAsync(harness.Command, step, CommandTimeout, harness.Environment(host)).ConfigureAwait(false);
            // Adding a marketplace that's already there fails; the install that follows is what counts.
            if (!run.Succeeded && step != harness.ConfigureSteps[^1])
                logger.LogDebug("{Command} {Step}: {Reason}", harness.Command, string.Join(' ', step), run.FailureSummary);
            else if (!run.Succeeded)
                return new(kind, ConfigureStatus.Failed, run.FailureSummary);
        }
        try
        {
            harness.AfterConfigure(host);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(kind, ConfigureStatus.Failed, ex.Message);
        }
        return new(kind, ConfigureStatus.Configured);
    }

    /// <summary>Healthy: installed, the plugin enabled and shipping skills, and with <paramref name="deep"/>
    /// each MCP server the plugin declares answering a handshake. A plugin that declares none has nothing to probe.</summary>
    public async Task<HarnessHealth> ValidateAsync(HarnessKind kind, bool deep)
    {
        if (!IsInstalled(kind))
            return new(kind, false, 0, [], "not installed");
        var plugin = await PluginAsync(kind).ConfigureAwait(false);
        if (plugin is not { Enabled: true })
            return new(kind, false, 0, [], plugin is null ? "plugin not installed" : "plugin disabled");
        var skills = SkillCount(plugin);
        if (skills == 0)
            return new(kind, false, 0, [], "plugin has no skills");
        if (!deep)
            return new(kind, true, skills, []);

        var servers = McpServer.DeclaredIn(plugin.Directory!);
        var probes = new List<McpProbeResult>();
        foreach (var server in servers)
            probes.Add(await McpProbe.ProbeAsync(server, processes.FindOnPath(server.Command)).ConfigureAwait(false));
        return new(kind, probes.All(p => p.Healthy), skills, probes, servers.Count == 0 ? "plugin declares no MCP servers" : null);
    }

    private async Task<PluginState?> PluginAsync(HarnessKind kind)
    {
        var harness = Harnesses.Get(kind);
        var run = await processes.RunAsync(harness.Command, ["plugin", "list", "--json"], CommandTimeout, harness.Environment(host)).ConfigureAwait(false);
        return run.Succeeded ? harness.ParsePluginList(run.StandardOutput, host) : null;
    }

    private int SkillCount(PluginState? plugin) =>
        plugin?.Directory is { } directory && host.DirectoryExists(Path.Combine(directory, "skills"))
            ? Directory.GetDirectories(Path.Combine(directory, "skills")).Length
            : 0;

    public static string Id(HarnessKind kind) => kind.ToString().ToLowerInvariant();

    public static IReadOnlyList<HarnessKind> Parse(IEnumerable<string> names) =>
        names.Select(name => Enum.TryParse<HarnessKind>(name, ignoreCase: true, out var kind) && Enum.IsDefined(kind)
                ? kind
                : throw new InvalidInputException($"Unknown harness '{name}'. Known: {string.Join(", ", Enum.GetValues<HarnessKind>().Select(Id))}."))
            .Distinct()
            .ToList();
}
