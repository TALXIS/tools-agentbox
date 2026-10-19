using System.Text.Json;
using TALXIS.AgentBox.CLI.Host;
using TALXIS.AgentBox.CLI.Prerequisites;

namespace TALXIS.AgentBox.CLI.Tests;

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
        var inCatalog = PrerequisiteCatalog.All.Where(p => p.Feature is not null).Select(p => p.Feature!).Order();
        Assert.Equal(inManifest, inCatalog);
    }

    [Fact]
    public void On_a_workstation_everything_has_a_manual_command() =>
        Assert.All(Enum.GetValues<OsKind>(), os =>
            Assert.All(PrerequisiteCatalog.All.Where(p => !p.ContainerOnly), p => Assert.NotNull(p.Manual.For(os))));
}
