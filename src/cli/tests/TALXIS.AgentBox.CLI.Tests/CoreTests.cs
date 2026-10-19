using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Features;
using TALXIS.AgentBox.CLI.Core.Harness;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Prerequisites;
using TALXIS.AgentBox.CLI.Core.Registry;

namespace TALXIS.AgentBox.CLI.Tests;

public class UpdaterTests
{
    private static readonly Core.Prerequisites.Prerequisite Tool = new()
    {
        Id = "tool", Description = "A dotnet tool", Command = "tool", Kind = PrerequisiteKind.DotnetTool,
        Package = "Some.Tool", Feature = "example/features/tool", LinuxAutoUpdateMarker = "/etc/devcontainer-tool/auto-update",
    };

    /// <summary>A host with Some.Tool <paramref name="version"/> in the dotnet tool store.</summary>
    private static (FakeHost Host, FakeProcesses Processes) WithInstalledTool(string version, bool followsLatest)
    {
        var host = new FakeHost();
        Directory.CreateDirectory(Path.Combine(host.HomeDirectory, ".dotnet", "tools", ".store", "some.tool", version));
        if (followsLatest)
            host.Files.Add(Tool.LinuxAutoUpdateMarker!);
        var processes = new FakeProcesses();
        processes.OnPath["tool"] = "/usr/local/bin/tool";
        return (host, processes);
    }

    private static async Task<UpdateResult> UpdateAsync(FakeHost host, FakeProcesses processes, FakeRegistry registry)
    {
        using var harness = new CommandHarness(host, processes, registry);
        var run = await AgentBoxServices.Current.Updater(NullLogger.Instance).UpdateAsync([Tool], TimeSpan.FromSeconds(30), CancellationToken.None);
        return Assert.Single(run.Results);
    }

    [Fact]
    public async Task Updates_to_the_latest_version_when_newer()
    {
        var (host, processes) = WithInstalledTool("1.0.0", followsLatest: true);
        var registry = new FakeRegistry();
        registry.Latest["Some.Tool"] = "1.2.0";

        var result = await UpdateAsync(host, processes, registry);

        Assert.Equal(UpdateAction.Updated, result.Action);
        Assert.Equal(("1.0.0", "1.2.0"), (result.From, result.To));
        var call = Assert.Single(processes.Calls);
        Assert.Equal(["tool", "update", "--global", "Some.Tool", "--version", "1.2.0"], call.Args);
    }

    [Fact]
    public async Task Does_nothing_when_current()
    {
        var (host, processes) = WithInstalledTool("1.2.0", followsLatest: true);
        var registry = new FakeRegistry();
        registry.Latest["Some.Tool"] = "1.2.0";
        Assert.Equal(UpdateAction.UpToDate, (await UpdateAsync(host, processes, registry)).Action);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task Leaves_pinned_tools_alone()
    {
        var (host, processes) = WithInstalledTool("1.0.0", followsLatest: false);
        var registry = new FakeRegistry();
        registry.Latest["Some.Tool"] = "1.2.0";
        Assert.Equal(UpdateAction.NotManaged, (await UpdateAsync(host, processes, registry)).Action);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task Offline_is_not_a_failure()
    {
        var (host, processes) = WithInstalledTool("1.0.0", followsLatest: true);
        var result = await UpdateAsync(host, processes, new FakeRegistry { Offline = true });
        Assert.Equal(UpdateAction.UpdateCheckSkipped, result.Action);
        Assert.Empty(processes.Calls);
    }
}

public class RegistryTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task NuGet_latest_ignores_prereleases()
    {
        var handler = new StubHandler(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-flatcontainer-index.json")));
        var client = new PackageRegistryClient(new HttpClient(handler));
        var latest = await client.GetLatestNuGetVersionAsync("TALXIS.CLI", CancellationToken.None);
        Assert.Equal("1.21.1", latest?.ToNormalizedString());
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/talxis.cli/index.json", handler.Requested?.ToString());
    }

    [Fact]
    public async Task Npm_latest_reads_the_dist_tag()
    {
        var handler = new StubHandler(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "npm-latest.json")));
        var client = new PackageRegistryClient(new HttpClient(handler));
        var latest = await client.GetLatestNpmVersionAsync("@scope/package", CancellationToken.None);
        Assert.Equal("1.3.0", latest?.ToNormalizedString());
        Assert.Equal("https://registry.npmjs.org/@scope%2Fpackage/latest", handler.Requested?.AbsoluteUri);
    }
}

public class FeatureTests
{
    private const string Lock = """
        {"features": [
          {"id": "ghcr.io/devcontainers/features/node@sha256:abc", "options": {"version": "22"}},
          {"id": "ghcr.io/talxis/tools-agentbox/pa-cli@sha256:def", "options": {"version": "latest", "install-dir": "x"}}
        ]}
        """;

    [Fact]
    public void Parses_the_lockfile()
    {
        var parsed = FeatureLock.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Lock)))!;
        Assert.Equal(2, parsed.Features.Count);
        var pa = parsed.Features[1];
        Assert.Equal("ghcr.io", pa.Registry);
        Assert.Equal("talxis/tools-agentbox/pa-cli", pa.Path);
        Assert.Equal("sha256:def", pa.Digest);
    }

    [Fact]
    public void Options_become_upper_case_environment_variables()
    {
        if (!OperatingSystem.IsLinux())
            return; // Features only install on Linux.
        var parsed = FeatureLock.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Lock)))!;
        var environment = FeatureInstaller.OptionEnvironment(parsed.Features[1]);
        Assert.Equal("latest", environment["VERSION"]);
        Assert.Equal("x", environment["INSTALL_DIR"]);
        Assert.Contains("_REMOTE_USER", environment.Keys);
    }

    [Fact]
    public void Declared_defaults_fill_in_options_the_lockfile_leaves_out()
    {
        if (!OperatingSystem.IsLinux())
            return; // Features only install on Linux.
        var featureJson = Path.Combine(Directory.CreateTempSubdirectory("agentbox-feature-").FullName, "devcontainer-feature.json");
        File.WriteAllText(featureJson, """
            {"id": "git", "options": {
              "version": {"type": "string", "default": "os-provided"},
              "ppa": {"type": "boolean", "default": true},
              "install-dir": {"type": "string", "default": "/usr"}
            }}
            """);
        var parsed = FeatureLock.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Lock)))!;
        var environment = FeatureInstaller.OptionEnvironment(parsed.Features[1], FeatureInstaller.DeclaredDefaults(featureJson));
        Assert.Equal("latest", environment["VERSION"]);   // set in the lockfile: wins
        Assert.Equal("x", environment["INSTALL_DIR"]);
        Assert.Equal("true", environment["PPA"]);         // only declared: default
    }
}

public class HarnessTests
{
    [Theory]
    [InlineData("""[{"id":"implement@talxis","version":"1.0.0"}]""", true)]
    [InlineData("""[{"id":"other@market"}]""", false)]
    [InlineData("[]", false)]
    [InlineData("not json", false)]
    public void Detects_the_plugin_in_claude_plugin_list(string json, bool expected) =>
        Assert.Equal(expected, HarnessService.ClaudeListHasPlugin(json));
}
