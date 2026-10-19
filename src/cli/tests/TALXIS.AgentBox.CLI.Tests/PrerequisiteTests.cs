using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.AgentBox.CLI.Prerequisites;

namespace TALXIS.AgentBox.CLI.Tests;

public class PrerequisiteTests
{
    private static readonly Prerequisite Tool = new()
    {
        Id = "tool", Description = "A dotnet tool", Command = "tool", Kind = PrerequisiteKind.DotnetTool,
        Package = "Some.Tool", Feature = "talxis/tools-agentbox/tool-cli",
    };

    /// <summary>A host with Some.Tool <paramref name="version"/> installed by its Feature.</summary>
    private static (FakeHost Host, FakeProcesses Processes) WithTool(string version, bool followsLatest)
    {
        var host = new FakeHost();
        Directory.CreateDirectory(Path.Combine(host.HomeDirectory, ".dotnet", "tools", ".store", "some.tool", version));
        if (followsLatest)
            host.Files.Add(Tool.FollowsLatestMarker!);
        var processes = new FakeProcesses();
        processes.OnPath["tool"] = "/usr/local/bin/tool";
        return (host, processes);
    }

    private static async Task<UpdateResult> UpdateAsync(FakeHost host, FakeProcesses processes, FakeRegistry registry)
    {
        using var _ = new CommandHarness(host, processes, registry);
        return Assert.Single(await AgentBoxServices.Current.Updater(NullLogger.Instance).UpdateAsync([Tool], CancellationToken.None));
    }

    [Fact]
    public async Task Update_installs_the_newer_version()
    {
        var (host, processes) = WithTool("1.0.0", followsLatest: true);
        var registry = new FakeRegistry { Latest = { ["Some.Tool"] = "1.2.0" } };
        var result = await UpdateAsync(host, processes, registry);
        Assert.Equal(new UpdateResult("tool", UpdateAction.Updated, "1.0.0", "1.2.0"), result);
        Assert.Equal(["dotnet tool update --global Some.Tool --version 1.2.0"], processes.Calls);
    }

    [Fact]
    public async Task Update_does_nothing_when_current()
    {
        var (host, processes) = WithTool("1.2.0", followsLatest: true);
        Assert.Equal(UpdateAction.UpToDate, (await UpdateAsync(host, processes, new FakeRegistry { Latest = { ["Some.Tool"] = "1.2.0" } })).Action);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task Update_leaves_pinned_tools_alone()
    {
        var (host, processes) = WithTool("1.0.0", followsLatest: false);
        Assert.Equal(UpdateAction.NotManaged, (await UpdateAsync(host, processes, new FakeRegistry { Latest = { ["Some.Tool"] = "1.2.0" } })).Action);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task Update_treats_offline_as_normal()
    {
        var (host, processes) = WithTool("1.0.0", followsLatest: true);
        Assert.Equal(UpdateAction.Offline, (await UpdateAsync(host, processes, new FakeRegistry { Offline = true })).Action);
    }

    [Fact]
    public async Task Validate_exits_0_when_everything_is_present_and_3_when_something_is_missing()
    {
        var processes = FakeProcesses.WithEverything();
        using (var harness = new CommandHarness(new FakeHost(), processes))
            Assert.Equal(0, await new PrerequisiteValidateCommand { Format = "json", Ids = ["git", "dotnet", "pac"] }.RunAsync());

        processes.OnPath.Remove("az");
        using (var harness = new CommandHarness(new FakeHost(), processes))
        {
            Assert.Equal(3, await new PrerequisiteValidateCommand { Format = "json", Ids = ["git", "az"] }.RunAsync());
            using var json = JsonDocument.Parse(harness.Output);
            Assert.Equal(["ok", "missing"], json.RootElement.EnumerateArray().Select(r => r.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task An_old_dotnet_SDK_counts_as_missing()
    {
        var processes = FakeProcesses.WithEverything();
        processes.Handler = (_, _) => new(0, "8.0.400 [/usr/share/dotnet/sdk]\n", "", false);
        using var harness = new CommandHarness(new FakeHost(), processes);
        Assert.Equal(3, await new PrerequisiteValidateCommand { Format = "json", Ids = ["dotnet"] }.RunAsync());
    }

    [Fact]
    public async Task Node_older_than_22_counts_as_missing()
    {
        var processes = FakeProcesses.WithEverything();
        processes.Handler = (_, _) => new(0, "v20.18.0\n", "", false);
        using var harness = new CommandHarness(new FakeHost(), processes);
        Assert.Equal(3, await new PrerequisiteValidateCommand { Format = "json", Ids = ["node"] }.RunAsync());
    }

    [Fact]
    public async Task Container_only_prerequisites_are_not_needed_on_a_workstation()
    {
        var processes = FakeProcesses.WithEverything();
        processes.OnPath.Remove("agentbox-keyring-init");
        using var harness = new CommandHarness(new FakeHost { IsContainer = false }, processes);
        Assert.Equal(0, await new PrerequisiteValidateCommand { Format = "json", Ids = ["keyring"] }.RunAsync());
        Assert.Contains("\"not-needed\"", harness.Output);
    }

    [Fact]
    public async Task Bad_input_exits_2()
    {
        using var harness = new CommandHarness(new FakeHost());
        Assert.Equal(2, await new PrerequisiteValidateCommand { Format = "json", Ids = ["nope"] }.RunAsync());
        Assert.Equal(2, await new PrerequisiteListCommand { Format = "yaml" }.RunAsync());
    }

    [Fact]
    public async Task On_a_workstation_install_reports_the_manual_command_for_system_tools()
    {
        var processes = new FakeProcesses();
        using var harness = new CommandHarness(new FakeHost { Os = Host.OsKind.Windows, IsContainer = false }, processes);
        Assert.Equal(0, await new PrerequisiteInstallCommand { Format = "json", Ids = ["az"] }.RunAsync());
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal("manual", json.RootElement[0].GetProperty("action").GetString());
        Assert.Equal("winget install --id Microsoft.AzureCLI -e", json.RootElement[0].GetProperty("detail").GetString());
        Assert.Empty(processes.Calls);
    }
}
