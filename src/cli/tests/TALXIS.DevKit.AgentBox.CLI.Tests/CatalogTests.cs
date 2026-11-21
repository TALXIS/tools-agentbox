using System.Text.Json;
using TALXIS.DevKit.AgentBox.CLI.Host;
using TALXIS.DevKit.AgentBox.CLI.Prerequisites;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class CatalogTests
{
    private const string Manifest = "src/container/agentbox-features.json";

    [Fact]
    public void The_catalog_and_the_Feature_manifest_list_the_same_Features()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Repo.File(Manifest)));
        var inManifest = manifest.RootElement.GetProperty("features").EnumerateObject()
            .Select(f => f.Name[(f.Name.IndexOf('/') + 1)..f.Name.LastIndexOf(':')])
            .Order();
        var inCatalog = PrerequisiteCatalog.All.Where(p => p.Feature is not null).Select(p => p.Feature!).Distinct().Order(); // one Feature can give several rows
        Assert.Equal(inManifest, inCatalog);
    }

    [Fact]
    public void Every_version_the_manifest_asks_for_can_be_checked() =>
        Assert.All(PrerequisiteCatalog.All.Where(p => p.Kind != PrerequisiteKind.PlaywrightBrowser && !ToolManifest.Embedded.SpecFor(p).IsAny), p =>
            Assert.True(p.Kind is PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool || p.VersionProbe is not null,
                $"{p.Id}: the manifest asks for {ToolManifest.Embedded.SpecFor(p)}, but the catalog has no way to read its installed version"));

    [Fact]
    public void On_a_workstation_everything_has_a_manual_command() =>
        Assert.All(Enum.GetValues<OsKind>(), os =>
            Assert.All(PrerequisiteCatalog.All.Where(p => !p.ContainerOnly), p => Assert.NotNull(p.Manual.For(os))));
}
