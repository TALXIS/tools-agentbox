using System.Formats.Tar;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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
}

// Features only install on Linux, which is where the tests run.
[SupportedOSPlatform("linux")]
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
    public void Options_become_environment_variables_and_declared_defaults_fill_the_gaps()
    {
        var featureJson = Path.Combine(Directory.CreateTempSubdirectory("feature-").FullName, "devcontainer-feature.json");
        File.WriteAllText(featureJson, """{"options": {"version": {"default": "os-provided"}, "ppa": {"default": true}}}""");

        var environment = FeatureInstaller.OptionEnvironment(Parse().Features[1], FeatureInstaller.DeclaredDefaults(featureJson));

        Assert.Equal("latest", environment["VERSION"]); // the lockfile wins over the default
        Assert.Equal("x", environment["INSTALL_DIR"]);
        Assert.Equal("true", environment["PPA"]);
    }

    /// <summary>A registry serving one Feature: its token, its manifest, and the layer the manifest names.</summary>
    private sealed class FeatureRegistry(byte[] manifest, byte[] layer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path == "/token" ? Encoding.UTF8.GetBytes("""{"token":"t"}""") : path.Contains("/manifests/") ? manifest : layer;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static string Sha256(byte[] content) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] FeatureLayer()
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, leaveOpen: true))
        {
            foreach (var (name, content) in new[] { ("devcontainer-feature.json", "{}"), ("install.sh", "echo installed") })
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        }
        return tar.ToArray();
    }

    /// <summary>Downloads a Feature pinned to <paramref name="manifest"/>'s digest, served with <paramref name="servedLayer"/>.</summary>
    private static Task<string> Download(byte[] manifest, byte[] servedLayer)
    {
        var installer = new FeatureInstaller(new FakeProcesses(), new HttpClient(new FeatureRegistry(manifest, servedLayer)), NullLogger.Instance);
        var feature = new LockedFeature($"ghcr.io/talxis/tools-agentbox/x@{Sha256(manifest)}", null);
        return installer.DownloadAsync(feature, Directory.CreateTempSubdirectory("feature-").FullName, CancellationToken.None);
    }

    [Fact]
    public async Task Only_the_pinned_Feature_is_unpacked()
    {
        var layer = FeatureLayer();
        var manifest = Encoding.UTF8.GetBytes($$"""{"layers":[{"digest":"{{Sha256(layer)}}"}]}""");

        Assert.True(File.Exists(Path.Combine(await Download(manifest, layer), "install.sh")));

        var tampered = FeatureLayer().Concat(new byte[512]).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => Download(manifest, tampered));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("say \"hi\" $(id) `id` \\", "say \\\"hi\\\" \\$(id) \\`id\\` \\\\")]
    [InlineData("${PATH}:/opt/bin", "${PATH}:/opt/bin")]
    public void ContainerEnv_values_stay_literal_in_the_profile_script(string value, string quoted) =>
        Assert.Equal(quoted, FeatureInstaller.ShellDoubleQuoted(value));
}
