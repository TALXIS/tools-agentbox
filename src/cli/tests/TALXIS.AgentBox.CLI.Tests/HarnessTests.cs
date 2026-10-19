using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.AgentBox.CLI.Harness;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Tests;

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
        Assert.False((await service.ValidateAsync(HarnessKind.Claude, deep: true)).Healthy);
    }
}

public class McpProbeTests
{
    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""")] // stops answering after initialize
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"message":"boom"}}""")] // answers with an error
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""", """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""")] // offers no tools
    public async Task A_server_that_does_not_offer_tools_is_unhealthy(params string[] responses)
    {
        // Servers log and send notifications before answering; both must be skipped.
        var output = new StringReader(string.Join('\n', new[] { "info: starting", """{"jsonrpc":"2.0","method":"notifications/message"}""" }.Concat(responses)));
        Assert.False((await McpProbe.HandshakeAsync("txc", new StringWriter(), output, CancellationToken.None)).Healthy);
    }
}
