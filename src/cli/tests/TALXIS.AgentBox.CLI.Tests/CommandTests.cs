using System.Reflection;
using System.Text.Json;
using DotMake.CommandLine;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Prerequisites;
using TALXIS.AgentBox.CLI.Devcontainer;
using TALXIS.AgentBox.CLI.Harness;
using TALXIS.AgentBox.CLI.Prerequisite;

namespace TALXIS.AgentBox.CLI.Tests;

public class CommandTests
{
    /// <summary>A host where every catalog command is on PATH.</summary>
    private static FakeProcesses AllOnPath()
    {
        var processes = new FakeProcesses();
        foreach (var p in PrerequisiteCatalog.All)
            processes.OnPath[p.Command] = "/usr/bin/" + p.Command;
        return processes;
    }

    [Fact]
    public async Task Validate_exits_0_when_everything_is_present()
    {
        using var harness = new CommandHarness(new FakeHost(), AllOnPath());
        var exit = await new PrerequisiteValidateCliCommand { Format = "json", Ids = ["git", "az", "pa"] }.RunAsync();
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal(["git", "az", "pa"], json.RootElement.EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.All(json.RootElement.EnumerateArray(), r => Assert.Equal("ok", r.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Validate_exits_3_when_something_is_missing()
    {
        var processes = AllOnPath();
        processes.OnPath.Remove("az");
        using var harness = new CommandHarness(new FakeHost(), processes);
        var exit = await new PrerequisiteValidateCliCommand { Format = "json" }.RunAsync();
        Assert.Equal(3, exit);
        using var json = JsonDocument.Parse(harness.Output);
        var az = json.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "az");
        Assert.Equal("missing", az.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Container_only_prerequisites_do_not_fail_a_workstation()
    {
        var processes = AllOnPath();
        processes.OnPath.Remove("agentbox-keyring-init");
        using var harness = new CommandHarness(new FakeHost { HostClass = HostClass.Workstation, IsRoot = false }, processes);
        Assert.Equal(0, await new PrerequisiteValidateCliCommand { Format = "json", Ids = ["git", "keyring"] }.RunAsync());
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal("not-applicable", json.RootElement[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_ids_are_input_errors()
    {
        using var harness = new CommandHarness(new FakeHost());
        Assert.Equal(2, await new PrerequisiteValidateCliCommand { Format = "json", Ids = ["nope"] }.RunAsync());
        Assert.Equal(2, await new HarnessConfigureCliCommand { Format = "json", Ids = ["nope"] }.RunAsync());
    }

    [Fact]
    public async Task Unknown_format_is_an_input_error()
    {
        using var harness = new CommandHarness(new FakeHost());
        Assert.Equal(2, await new PrerequisiteListCliCommand { Format = "yaml" }.RunAsync());
    }

    [Fact]
    public async Task List_shows_no_install_mechanics()
    {
        using var harness = new CommandHarness(new FakeHost { Os = OsKind.Windows, HostClass = HostClass.Workstation, IsRoot = false });
        Assert.Equal(0, await new PrerequisiteListCliCommand { Format = "json" }.RunAsync());
        using var json = JsonDocument.Parse(harness.Output);
        var names = json.RootElement.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name)).ToHashSet();
        Assert.Subset(new HashSet<string> { "id", "description", "installedByAgentBox", "manualCommand" }, names);
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), r => r.GetProperty("id").GetString() == "keyring");
    }

    [Fact]
    public async Task Devcontainer_create_writes_the_minimal_file_and_refuses_to_overwrite()
    {
        using var harness = new CommandHarness(new FakeHost());
        var repo = Directory.CreateTempSubdirectory("agentbox-repo-").FullName;
        var file = Path.Combine(repo, ".devcontainer", "devcontainer.json");

        Assert.Equal(0, await new DevcontainerCreateCliCommand { Format = "json", Directory = repo, Name = "Project" }.RunAsync());
        using (var json = JsonDocument.Parse(File.ReadAllText(file)))
        {
            Assert.Equal(["name", "image"], json.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal("Project", json.RootElement.GetProperty("name").GetString());
            Assert.Equal("ghcr.io/talxis/tools-agentbox/image:latest", json.RootElement.GetProperty("image").GetString());
        }

        Assert.Equal(2, await new DevcontainerCreateCliCommand { Format = "json", Directory = repo, Name = "Other" }.RunAsync());
        Assert.Contains("\"Project\"", File.ReadAllText(file));

        Assert.Equal(0, await new DevcontainerCreateCliCommand { Format = "json", Directory = repo, Name = "Other", ForceOverwrite = true }.RunAsync());
        Assert.Contains("\"Other\"", File.ReadAllText(file));
    }

    [Fact]
    public async Task Harness_configure_honours_AGENTBOX_HARNESS_none()
    {
        var host = new FakeHost();
        host.Environment["AGENTBOX_HARNESS"] = "none";
        var processes = AllOnPath();
        using var harness = new CommandHarness(host, processes);
        Assert.Equal(0, await new HarnessConfigureCliCommand { Format = "json" }.RunAsync());
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public void Every_leaf_command_inherits_the_base_and_declares_exactly_one_safety_attribute()
    {
        var safety = new[] { typeof(CliReadOnlyAttribute), typeof(CliIdempotentAttribute), typeof(CliDestructiveAttribute) };
        var commands = typeof(PrerequisiteCliCommand).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<CliCommandAttribute>() is not null)
            .ToList();
        var leaves = commands.Where(t => t.GetCustomAttribute<CliCommandAttribute>()!.Children is not { Length: > 0 }).ToList();

        Assert.NotEmpty(leaves);
        foreach (var leaf in leaves)
        {
            Assert.True(typeof(AgentBoxLeafCommand).IsAssignableFrom(leaf), $"{leaf.Name} must inherit AgentBoxLeafCommand");
            Assert.Equal(1, safety.Count(a => leaf.GetCustomAttribute(a) is not null));
        }
    }
}
