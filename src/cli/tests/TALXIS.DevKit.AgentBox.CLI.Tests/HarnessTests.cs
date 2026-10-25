using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.DevKit.AgentBox.CLI.Harness;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class HarnessTests
{
    [Theory]
    [InlineData("claude copilot", "copilot", false, true, "copilot", false)] // names given win
    [InlineData("claude copilot", "", true, true, "claude copilot", false)] // a container never asks
    [InlineData("claude copilot", "", false, true, "copilot", true)] // a person with both is asked
    [InlineData("claude", "", false, true, "claude", false)] // one installed: nothing to ask
    [InlineData("claude copilot", "", false, false, "claude copilot", false)] // no one at the terminal
    public void Selects_the_named_harnesses_else_the_installed_ones(string installed, string names, bool container, bool interactive, string expected, bool asked)
    {
        var processes = new FakeProcesses();
        foreach (var command in installed.Split(' '))
            processes.OnPath[command] = "/usr/bin/" + command;
        var prompt = new FakePrompt("copilot");
        var service = new HarnessService(new FakeHost { IsContainer = container, IsInteractive = interactive }, processes, prompt, NullLogger.Instance);

        var selected = service.Select(names.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(expected, string.Join(' ', selected.Select(HarnessService.Id)));
        Assert.Equal(asked ? 1 : 0, prompt.Asked);
    }

    [Fact]
    public void Copilot_settings_keep_what_was_there_and_follow_COPILOT_HOME()
    {
        var host = new FakeHost();
        var copilotHome = Path.Combine(host.HomeDirectory, "elsewhere");
        host.Environment["COPILOT_HOME"] = copilotHome;
        Directory.CreateDirectory(copilotHome);
        File.WriteAllText(Path.Combine(copilotHome, "settings.json"), """
            {
              // the user's own setting
              "theme": "dark",
            }
            """);

        new CopilotHarness().AfterConfigure(host);

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(copilotHome, "settings.json")))!;
        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.True(settings["enabledPlugins"]!["implement@talxis"]!.GetValue<bool>());
        Assert.Equal(Path.Combine(copilotHome, "installed-plugins", "talxis", "implement"),
            new CopilotHarness().ParsePluginList("""[{"name":"implement","marketplace":"talxis","enabled":true}]""", host)!.Directory);
    }

    [Fact]
    public void An_empty_Copilot_settings_file_counts_as_empty_settings()
    {
        var host = new FakeHost();
        var file = Path.Combine(host.HomeDirectory, ".copilot", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, " \n");

        new CopilotHarness().AfterConfigure(host);

        Assert.True(JsonNode.Parse(File.ReadAllText(file))!["enabledPlugins"]!["implement@talxis"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_broken_Copilot_settings_file_is_a_reported_failure()
    {
        var host = new FakeHost();
        Directory.CreateDirectory(Path.Combine(host.HomeDirectory, ".copilot"));
        File.WriteAllText(Path.Combine(host.HomeDirectory, ".copilot", "settings.json"), """{ "theme": """);
        var processes = new FakeProcesses();
        processes.OnPath["copilot"] = "/usr/bin/copilot";

        var result = await new HarnessService(host, processes, new FakePrompt(), NullLogger.Instance).ConfigureAsync(HarnessKind.Copilot);

        Assert.Equal(ConfigureStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Validate_with_no_harness_installed_is_unhealthy()
    {
        using var harness = new CommandHarness(new FakeHost());
        Assert.Equal(3, await new HarnessValidateCommand { Format = "json" }.InvokeAsync());
    }

    private static string ClaudeList(string version) =>
        $$"""[{"id":"implement@talxis","enabled":true,"version":"{{version}}","installPath":"/x/{{version}}"}]""";

    private static string CopilotList(string version) =>
        $$"""[{"name":"implement","marketplace":"talxis","version":"{{version}}","enabled":true},{"name":"browser-use","marketplace":"","version":"0.0.4","enabled":false}]""";

    /// <summary>A harness whose plugin list shows <paramref name="listed"/> until its update command runs, then <paramref name="updated"/>.</summary>
    private static (HarnessService Service, FakeProcesses Processes) ServiceWhere(string command, Func<string, string> list, string? listed, string? updated, int updateExit = 0, FakeHost? host = null)
    {
        var done = false;
        var processes = new FakeProcesses();
        processes.OnPath[command] = "/usr/bin/" + command;
        processes.Handler = (_, args) => string.Join(' ', args) switch
        {
            "plugin list --json" => new(0, listed is null ? "[]" : list(done ? updated! : listed), "", false),
            "plugin update implement@talxis" => (done = updateExit == 0) ? new(0, "", "", false) : new(updateExit, "", "boom", false),
            _ => new(0, "", "", false),
        };
        return (new HarnessService(host ?? new FakeHost(), processes, new FakePrompt(), NullLogger.Instance), processes);
    }

    [Theory]
    [InlineData("claude", HarnessKind.Claude)]
    [InlineData("copilot", HarnessKind.Copilot)]
    public async Task Update_reports_the_version_change(string command, HarnessKind kind)
    {
        var (service, processes) = ServiceWhere(command, command == "claude" ? ClaudeList : CopilotList, "0.4.2", "0.4.10");

        var result = await service.UpdateAsync(kind);

        Assert.Equal(UpdateStatus.Updated, result.Status);
        Assert.Contains("0.4.2 -> 0.4.10", result.Detail);
        Assert.Contains($"{command} plugin update implement@talxis", processes.Calls);
    }

    [Fact]
    public async Task Update_with_nothing_newer_is_current()
    {
        var (service, _) = ServiceWhere("copilot", CopilotList, "0.4.10", "0.4.10");

        var result = await service.UpdateAsync(HarnessKind.Copilot);

        Assert.Equal(UpdateStatus.Current, result.Status);
        Assert.Equal("0.4.10", result.Detail);
    }

    [Fact]
    public async Task Update_leaves_a_harness_without_the_plugin_alone()
    {
        var (service, processes) = ServiceWhere("claude", ClaudeList, null, null);

        var result = await service.UpdateAsync(HarnessKind.Claude);

        Assert.Equal(UpdateStatus.NotSetUp, result.Status);
        Assert.DoesNotContain(processes.Calls, c => c.Contains("plugin update") || c.Contains("plugin install"));
    }

    [Fact]
    public async Task Update_reports_a_failing_update()
    {
        var (service, _) = ServiceWhere("claude", ClaudeList, "0.4.2", "0.4.10", updateExit: 1);

        Assert.Equal(UpdateStatus.Failed, (await service.UpdateAsync(HarnessKind.Claude)).Status);
    }

    [Fact]
    public async Task Update_of_a_harness_that_is_not_installed_does_nothing()
    {
        var service = new HarnessService(new FakeHost(), new FakeProcesses(), new FakePrompt(), NullLogger.Instance);

        Assert.Equal(UpdateStatus.NotInstalled, (await service.UpdateAsync(HarnessKind.Claude)).Status);
    }

    [Fact]
    public async Task Update_gives_Claude_the_seed_where_the_environment_has_one()
    {
        var host = new FakeHost();
        host.Environment["CLAUDE_CODE_PLUGIN_SEED_DIR"] = "/seed";
        var (service, processes) = ServiceWhere("claude", ClaudeList, "0.4.2", "0.4.10", host: host);

        await service.UpdateAsync(HarnessKind.Claude);

        var update = processes.Calls.IndexOf("claude plugin update implement@talxis");
        Assert.Equal("/seed", processes.Environments[update]!["CLAUDE_CODE_PLUGIN_CACHE_DIR"]);
    }

    [Fact]
    public async Task Updating_a_named_harness_that_is_not_installed_is_an_error_but_unnamed_ones_are_skipped()
    {
        using var harness = new CommandHarness(new FakeHost());

        Assert.Equal(0, await new HarnessUpdateCommand { Format = "json" }.InvokeAsync());
        Assert.Equal(1, await new HarnessUpdateCommand { Format = "json", Names = ["claude"] }.InvokeAsync());
    }

    [Fact]
    public void Claude_uses_the_seed_only_where_the_environment_has_one()
    {
        var host = new FakeHost();
        var reported = Path.Combine(host.HomeDirectory, ".claude", "plugins", "cache", "talxis", "implement");
        var pluginList = $$"""[{"id":"implement@talxis","enabled":true,"installPath":{{JsonSerializer.Serialize(reported)}}}]""";
        Assert.Null(new ClaudeHarness().Environment(host));

        host.Environment["CLAUDE_CODE_PLUGIN_SEED_DIR"] = "/seed:/other";
        Assert.Equal("/seed", new ClaudeHarness().Environment(host)!["CLAUDE_CODE_PLUGIN_CACHE_DIR"]);
        // Claude reports the plugin under ~/.claude/plugins although it was installed into the seed.
        Assert.Equal(Path.Combine("/seed", "cache", "talxis", "implement"), new ClaudeHarness().ParsePluginList(pluginList, host)!.Directory);
    }

    [Fact]
    public async Task Deep_validation_passes_when_the_plugin_declares_no_MCP_server()
    {
        var (service, _) = ServiceWithPlugin();

        Assert.True((await service.ValidateAsync(HarnessKind.Claude, deep: false)).Healthy);
        var deep = await service.ValidateAsync(HarnessKind.Claude, deep: true);
        Assert.True(deep.Healthy);
        Assert.Empty(deep.McpServers);
    }

    [Fact]
    public async Task Deep_validation_fails_when_a_declared_MCP_server_does_not_answer()
    {
        var (service, plugin) = ServiceWithPlugin();
        File.WriteAllText(Path.Combine(plugin, ".mcp.json"), """{"mcpServers":{"txc":{"command":"agentbox-no-such-server"}}}""");

        var deep = await service.ValidateAsync(HarnessKind.Claude, deep: true);
        Assert.False(deep.Healthy);
        Assert.Single(deep.McpServers);
        Assert.True((await service.ValidateAsync(HarnessKind.Claude, deep: false)).Healthy);
    }

    [Fact]
    public async Task Deep_validation_fails_when_a_declared_MCP_server_names_no_command()
    {
        var (service, plugin) = ServiceWithPlugin();
        File.WriteAllText(Path.Combine(plugin, ".mcp.json"), """{"mcpServers":{"txc":{"args":[]}}}""");

        var deep = await service.ValidateAsync(HarnessKind.Claude, deep: true);
        Assert.False(deep.Healthy);
        Assert.Equal("txc", Assert.Single(deep.McpServers).Server);
    }

    private static (HarnessService Service, string Plugin) ServiceWithPlugin()
    {
        var plugin = Directory.CreateTempSubdirectory("plugin-").FullName;
        Directory.CreateDirectory(Path.Combine(plugin, "skills", "a-skill"));
        var processes = new FakeProcesses
        {
            Handler = (_, _) => new(0, $$"""[{"id":"implement@talxis","enabled":true,"installPath":{{JsonSerializer.Serialize(plugin)}}}]""", "", false),
        };
        processes.OnPath["claude"] = "/usr/bin/claude";
        return (new HarnessService(new FakeHost(), processes, new FakePrompt(), NullLogger.Instance), plugin);
    }
}

public class McpProbeTests
{
    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""")] // stops answering after initialize
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"message":"boom"}}""")] // answers with an error
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""", """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""")] // offers no tools
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""")] // echoes the request back
    public async Task A_server_that_does_not_offer_tools_is_unhealthy(params string[] responses)
    {
        // Servers log and send notifications before answering; both must be skipped.
        var output = new StringReader(string.Join('\n', new[] { "info: starting", """{"jsonrpc":"2.0","method":"notifications/message"}""" }.Concat(responses)));
        Assert.False((await McpProbe.HandshakeAsync("txc", new StringWriter(), output, CancellationToken.None)).Healthy);
    }
}
