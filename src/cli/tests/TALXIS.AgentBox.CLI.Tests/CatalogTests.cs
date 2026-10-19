using System.Text.Json;
using TALXIS.AgentBox.CLI.Host;
using TALXIS.AgentBox.CLI.Prerequisites;

namespace TALXIS.AgentBox.CLI.Tests;

public class CatalogTests
{
    private const string Manifest = "src/container/templates/power-platform/.devcontainer/devcontainer.features.json";

    // The lockfile adds the dotnet Feature to the manifest's list (build/pack.sh).
    private const string DotnetFeature = "devcontainers/features/dotnet";

    [Fact]
    public void The_catalog_and_the_Feature_manifest_list_the_same_Features()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Repo.File(Manifest)));
        var inManifest = manifest.RootElement.GetProperty("features").EnumerateObject()
            .Select(f => f.Name[(f.Name.IndexOf('/') + 1)..f.Name.LastIndexOf(':')])
            .Append(DotnetFeature)
            .Order();
        var inCatalog = PrerequisiteCatalog.All.Where(p => p.Feature is not null).Select(p => p.Feature!).Order();
        Assert.Equal(inManifest, inCatalog);
    }

    [Fact]
    public void On_a_workstation_everything_has_a_manual_command() =>
        Assert.All(Enum.GetValues<OsKind>(), os =>
            Assert.All(PrerequisiteCatalog.All.Where(p => !p.ContainerOnly), p => Assert.NotNull(p.Manual.For(os))));
}
