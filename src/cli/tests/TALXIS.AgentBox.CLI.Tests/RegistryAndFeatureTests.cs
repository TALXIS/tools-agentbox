using System.Net;
using System.Text;
using TALXIS.AgentBox.CLI.Features;
using TALXIS.AgentBox.CLI.Registry;

namespace TALXIS.AgentBox.CLI.Tests;

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

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task NuGet_latest_ignores_prereleases()
    {
        var handler = new StubHandler(Fixture("nuget-flatcontainer-index.json"));
        var latest = await new PackageRegistryClient(new HttpClient(handler)).GetLatestNuGetVersionAsync("TALXIS.CLI", CancellationToken.None);
        Assert.Equal("1.21.1", latest?.ToNormalizedString());
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/talxis.cli/index.json", handler.Requested?.ToString());
    }

    [Fact]
    public async Task Npm_latest_reads_the_dist_tag_of_a_scoped_package()
    {
        var handler = new StubHandler(Fixture("npm-latest.json"));
        var latest = await new PackageRegistryClient(new HttpClient(handler)).GetLatestNpmVersionAsync("@scope/package", CancellationToken.None);
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

    private static FeatureLock Parse() => FeatureLock.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Lock)))!;

    [Fact]
    public void Parses_the_lockfile()
    {
        var pa = Parse().Features[1];
        Assert.Equal(("ghcr.io", "talxis/tools-agentbox/pa-cli", "sha256:def"), (pa.Registry, pa.Path, pa.Digest));
    }

    [Fact]
    public void Options_become_environment_variables_and_declared_defaults_fill_the_gaps()
    {
        if (!OperatingSystem.IsLinux())
            return; // Features only install on Linux.
        var featureJson = Path.Combine(Directory.CreateTempSubdirectory("feature-").FullName, "devcontainer-feature.json");
        File.WriteAllText(featureJson, """{"options": {"version": {"default": "os-provided"}, "ppa": {"default": true}}}""");

        var environment = FeatureInstaller.OptionEnvironment(Parse().Features[1], FeatureInstaller.DeclaredDefaults(featureJson));

        Assert.Equal("latest", environment["VERSION"]); // the lockfile wins over the default
        Assert.Equal("x", environment["INSTALL_DIR"]);
        Assert.Equal("true", environment["PPA"]);
        Assert.Contains("_REMOTE_USER", environment.Keys);
    }
}
