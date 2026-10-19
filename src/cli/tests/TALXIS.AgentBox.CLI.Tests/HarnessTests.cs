using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.AgentBox.CLI.Harness;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Tests;

public class HarnessTests
{
    private static HarnessService Service(FakeHost host, IPrompt prompt, params string[] installed)
    {
        var processes = new FakeProcesses();
        foreach (var command in installed)
            processes.OnPath[command] = "/usr/bin/" + command;
        return new HarnessService(host, processes, prompt, NullLogger.Instance);
    }

    [Fact]
    public void Names_given_win()
    {
        var prompt = new FakePrompt();
        Assert.Equal([HarnessKind.Copilot], Service(new FakeHost { IsContainer = false, IsInteractive = true }, prompt, "claude", "copilot").Select(["copilot"]));
        Assert.Equal(0, prompt.Asked);
    }

    [Fact]
    public void Containers_set_up_every_installed_harness_without_asking()
    {
        var prompt = new FakePrompt();
        Assert.Equal([HarnessKind.Claude, HarnessKind.Copilot], Service(new FakeHost { IsContainer = true, IsInteractive = true }, prompt, "claude", "copilot").Select(null));
        Assert.Equal(0, prompt.Asked);
    }

    [Fact]
    public void A_person_with_both_installed_is_asked()
    {
        var prompt = new FakePrompt("copilot");
        Assert.Equal([HarnessKind.Copilot], Service(new FakeHost { IsContainer = false, IsInteractive = true }, prompt, "claude", "copilot").Select(null));
        Assert.Equal(1, prompt.Asked);
    }

    [Fact]
    public void Nobody_is_asked_when_only_one_is_installed_or_no_one_is_at_the_terminal()
    {
        var prompt = new FakePrompt();
        Assert.Equal([HarnessKind.Claude], Service(new FakeHost { IsContainer = false, IsInteractive = true }, prompt, "claude").Select(null));
        Assert.Equal([HarnessKind.Claude, HarnessKind.Copilot], Service(new FakeHost { IsContainer = false, IsInteractive = false }, prompt, "claude", "copilot").Select(null));
        Assert.Equal(0, prompt.Asked);
    }

    [Fact]
    public void Unknown_names_are_rejected() =>
        Assert.Throws<ArgumentException>(() => HarnessService.Parse(["nope"]));

    [Fact]
    public void Reads_the_plugin_from_each_harness_plugin_list()
    {
        var host = new FakeHost();
        var claude = new ClaudeHarness().ParsePluginList("""[{"id":"implement@talxis","enabled":true,"installPath":"/p"}]""", host);
        Assert.Equal(new PluginState(true, "/p"), claude);

        var copilot = new CopilotHarness().ParsePluginList("""[{"name":"implement","marketplace":"talxis","enabled":false}]""", host);
        Assert.Equal(new PluginState(false, Path.Combine(host.HomeDirectory, ".copilot", "installed-plugins", "talxis", "implement")), copilot);

        Assert.Null(new ClaudeHarness().ParsePluginList("""[{"id":"other@market"}]""", host));
        Assert.Null(new CopilotHarness().ParsePluginList("not json", host));
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
    public void Claude_installs_into_the_seed_only_where_the_environment_has_one()
    {
        var host = new FakeHost();
        Assert.Null(new ClaudeHarness().Environment(host));
        host.Environment["CLAUDE_CODE_PLUGIN_SEED_DIR"] = "/seed:/other";
        Assert.Equal("/seed", new ClaudeHarness().Environment(host)!["CLAUDE_CODE_PLUGIN_CACHE_DIR"]);
    }

    [Fact]
    public async Task Deep_validation_fails_when_the_plugin_declares_no_MCP_server()
    {
        var plugin = Directory.CreateTempSubdirectory("plugin-").FullName;
        Directory.CreateDirectory(Path.Combine(plugin, "skills", "a-skill"));
        var processes = new FakeProcesses
        {
            Handler = (_, _) => new(0, $$"""[{"id":"implement@talxis","enabled":true,"installPath":{{JsonSerializer.Serialize(plugin)}}}]""", "", false),
        };
        processes.OnPath["claude"] = "/usr/bin/claude";
        var service = new HarnessService(new FakeHost(), processes, new FakePrompt(), NullLogger.Instance);

        Assert.True((await service.ValidateAsync(HarnessKind.Claude, deep: false)).Healthy);
        var deep = await service.ValidateAsync(HarnessKind.Claude, deep: true);
        Assert.False(deep.Healthy);
        Assert.Equal("plugin declares no MCP servers", deep.Detail);
    }

    [Fact]
    public void Reads_the_MCP_servers_a_plugin_declares()
    {
        var plugin = Directory.CreateTempSubdirectory("plugin-").FullName;
        File.WriteAllText(Path.Combine(plugin, "mcp.json"), """{"mcpServers":{"txc":{"type":"stdio","command":"dnx","args":["TALXIS.CLI.MCP","--yes"]}}}""");
        var server = Assert.Single(McpServer.DeclaredIn(plugin));
        Assert.Equal(("txc", "dnx"), (server.Name, server.Command));
        Assert.Equal(["TALXIS.CLI.MCP", "--yes"], server.Args);
    }
}

public class McpProbeTests
{
    /// <summary>Plays an MCP server's stdout: a log line, a notification, then the scripted responses.</summary>
    private static TextReader Server(params string[] responses) =>
        new StringReader(string.Join('\n', new[] { "info: starting", """{"jsonrpc":"2.0","method":"notifications/message"}""" }.Concat(responses)));

    [Fact]
    public async Task A_server_that_answers_with_tools_is_healthy()
    {
        var input = new StringWriter();
        var result = await McpProbe.HandshakeAsync("txc", input,
            Server("""{"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"txc"}}}""", """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"a"},{"name":"b"}]}}"""),
            CancellationToken.None);
        Assert.Equal(new McpProbeResult("txc", true, 2), result);
        Assert.Contains("\"initialize\"", input.ToString());
        Assert.Contains("\"tools/list\"", input.ToString());
    }

    [Fact]
    public async Task A_server_that_stops_answering_is_not()
    {
        var result = await McpProbe.HandshakeAsync("txc", new StringWriter(), Server("""{"jsonrpc":"2.0","id":1,"result":{}}"""), CancellationToken.None);
        Assert.False(result.Answered);
    }

    [Fact]
    public async Task An_error_response_is_reported()
    {
        var result = await McpProbe.HandshakeAsync("txc", new StringWriter(), Server("""{"jsonrpc":"2.0","id":1,"error":{"message":"boom"}}"""), CancellationToken.None);
        Assert.Equal(new McpProbeResult("txc", false, 0, "boom"), result);
    }
}
