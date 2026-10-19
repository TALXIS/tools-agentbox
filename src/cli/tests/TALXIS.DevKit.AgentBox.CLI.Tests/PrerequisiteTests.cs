using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.DevKit.AgentBox.CLI.Features;
using TALXIS.DevKit.AgentBox.CLI.Prerequisites;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

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

    /// <summary>Some.Tool 1.0.0 installed for every user, in its Feature's tool path, following latest.</summary>
    private static (Prerequisite Shared, FakeProcesses Processes, FakeRegistry Registry) SharedTool(FakeHost host)
    {
        var toolPath = Directory.CreateTempSubdirectory("tool-path-").FullName;
        Directory.CreateDirectory(Path.Combine(toolPath, ".store", "some.tool", "1.0.0"));
        host.Files.Add(Tool.FollowsLatestMarker!);
        var processes = new FakeProcesses();
        processes.OnPath["tool"] = "/usr/local/bin/tool";
        return (Tool with { ToolPath = toolPath }, processes, new FakeRegistry { Latest = { ["Some.Tool"] = "1.2.0" } });
    }

    [Fact]
    public async Task A_tool_installed_for_every_user_is_updated_where_it_is()
    {
        var host = new FakeHost { IsRoot = true };
        var (shared, processes, registry) = SharedTool(host);
        using var harness = new CommandHarness(host, processes, registry);

        var result = Assert.Single(await AgentBoxServices.Current.Updater(NullLogger.Instance).UpdateAsync([shared], CancellationToken.None));

        Assert.Equal(UpdateAction.Updated, result.Action);
        Assert.Equal([$"dotnet tool update Some.Tool --tool-path {shared.ToolPath} --version 1.2.0"], processes.Calls);
    }

    [Fact]
    public async Task Without_root_or_sudo_a_shared_tool_is_not_updated_somewhere_else()
    {
        var host = new FakeHost();
        var (shared, processes, registry) = SharedTool(host);
        using var harness = new CommandHarness(host, processes, registry);

        var result = Assert.Single(await AgentBoxServices.Current.Updater(NullLogger.Instance).UpdateAsync([shared], CancellationToken.None));

        Assert.Equal(UpdateAction.Failed, result.Action);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task A_user_without_the_templates_gets_them()
    {
        var processes = new FakeProcesses();
        processes.OnPath["dotnet"] = "/usr/bin/dotnet";
        using var harness = new CommandHarness(new FakeHost(), processes, new FakeRegistry { Latest = { ["TALXIS.DevKit.Templates.Dataverse"] = "1.25.0" } });

        var result = Assert.Single(await AgentBoxServices.Current.Updater(NullLogger.Instance).UpdateAsync(PrerequisiteCatalog.Select(["devkit-templates"]), CancellationToken.None));

        Assert.Equal(UpdateAction.Installed, result.Action);
        Assert.Equal(["dotnet new install TALXIS.DevKit.Templates.Dataverse::1.25.0"], processes.Calls);
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
    public async Task Validate_exits_3_when_something_is_missing()
    {
        var processes = FakeProcesses.WithEverything();
        processes.OnPath.Remove("az");
        using var harness = new CommandHarness(new FakeHost(), processes);
        Assert.Equal(3, await new PrerequisiteValidateCommand { Format = "json", Ids = ["git", "az"] }.InvokeAsync());
    }

    [Theory]
    [InlineData("dotnet", "8.0.400 [/usr/share/dotnet/sdk]")]
    [InlineData("node", "v20.18.0")]
    public async Task A_version_too_old_counts_as_missing(string id, string versionOutput)
    {
        var processes = FakeProcesses.WithEverything();
        processes.Handler = (_, _) => new(0, versionOutput + "\n", "", false);
        using var harness = new CommandHarness(new FakeHost(), processes);
        Assert.Equal(3, await new PrerequisiteValidateCommand { Format = "json", Ids = [id] }.InvokeAsync());
    }

    [Fact]
    public async Task Container_only_prerequisites_are_not_needed_on_a_workstation()
    {
        var processes = FakeProcesses.WithEverything();
        processes.OnPath.Remove("agentbox-keyring-init");
        using var harness = new CommandHarness(new FakeHost { IsContainer = false }, processes);
        Assert.Equal(0, await new PrerequisiteValidateCommand { Format = "json", Ids = ["keyring"] }.InvokeAsync());
    }

    [Fact]
    public async Task Bad_input_exits_2()
    {
        using var harness = new CommandHarness(new FakeHost());
        Assert.Equal(2, await new PrerequisiteValidateCommand { Format = "json", Ids = ["nope"] }.InvokeAsync());
        Assert.Equal(2, await new PrerequisiteListCommand { Format = "yaml" }.InvokeAsync());
    }

    [Fact]
    public async Task On_a_workstation_install_and_update_change_nothing()
    {
        var processes = new FakeProcesses();
        using (var harness = new CommandHarness(new FakeHost { IsContainer = false }, processes))
        {
            Assert.Equal(0, await new PrerequisiteInstallCommand { Format = "json" }.InvokeAsync());
            using var json = JsonDocument.Parse(harness.Output);
            Assert.All(json.RootElement.EnumerateArray(), r => Assert.Contains(r.GetProperty("action").GetString(), new[] { "manual", "present", "not-needed" }));
        }

        var (_, toolProcesses) = WithTool("1.0.0", followsLatest: true);
        var update = await UpdateAsync(new FakeHost { IsContainer = false }, toolProcesses, new FakeRegistry { Latest = { ["Some.Tool"] = "1.2.0" } });
        Assert.Equal(UpdateAction.NotManaged, update.Action);

        Assert.Empty(processes.Calls);
        Assert.Empty(toolProcesses.Calls);
    }

    private sealed class RecordingFeatureInstaller : IFeatureInstaller
    {
        public List<string> Installed { get; } = [];

        public Task<IReadOnlyList<FeatureResult>> InstallAsync(IReadOnlyList<LockedFeature> features, CancellationToken cancellationToken)
        {
            Installed.AddRange(features.Select(f => f.Path));
            return Task.FromResult<IReadOnlyList<FeatureResult>>(features.Select(f => new FeatureResult(f, true)).ToList());
        }
    }

    [Fact]
    public async Task In_a_container_a_prerequisite_comes_with_what_its_Feature_depends_on()
    {
        var features = new RecordingFeatureInstaller();
        var featureLock = new FeatureLock([
            new("ghcr.io/talxis/tools-agentbox/keyring@sha256:a", null),
            new("ghcr.io/talxis/tools-agentbox/pa-cli@sha256:b", null, ["talxis/tools-agentbox/keyring"]),
        ]);
        var services = new AgentBoxServices(new FakeHost { IsRoot = true }, new FakeProcesses(), new FakeRegistry(), new FakePrompt(), features, featureLock);

        await services.Installer(NullLogger.Instance).InstallAsync(PrerequisiteCatalog.Select(["pa"]), CancellationToken.None);

        Assert.Equal(["talxis/tools-agentbox/keyring", "talxis/tools-agentbox/pa-cli"], features.Installed);
    }
}
