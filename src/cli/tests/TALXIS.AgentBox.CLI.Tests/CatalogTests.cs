using System.Text.Json;
using TALXIS.AgentBox.CLI.Host;
using TALXIS.AgentBox.CLI.Prerequisites;

namespace TALXIS.AgentBox.CLI.Tests;

public class CatalogTests
{
    private const string Manifest = "src/container/templates/power-platform/.devcontainer/devcontainer.features.json";

    [Fact]
    public void Ids_are_unique() =>
        Assert.Equal(PrerequisiteCatalog.All.Count, PrerequisiteCatalog.All.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

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
    public void Everything_agentbox_installs_by_package_names_its_package() =>
        Assert.All(PrerequisiteCatalog.All.Where(p => p.Kind is PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool or PrerequisiteKind.DotnetTemplate),
            p => Assert.False(string.IsNullOrEmpty(p.Package), p.Id));

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void On_a_workstation_everything_has_a_manual_command(OsKind os) =>
        Assert.All(PrerequisiteCatalog.All.Where(p => !p.ContainerOnly), p => Assert.NotNull(p.Manual.For(os)));

    [Fact]
    public void Only_this_repos_Features_have_a_follows_latest_marker()
    {
        Assert.Equal("/etc/devcontainer-pac-cli/auto-update", PrerequisiteCatalog.All.Single(p => p.Id == "pac").FollowsLatestMarker);
        Assert.Null(PrerequisiteCatalog.All.Single(p => p.Id == "az").FollowsLatestMarker);
    }

    [Fact]
    public void Unknown_ids_are_rejected() =>
        Assert.Throws<ArgumentException>(() => PrerequisiteCatalog.Select(["nope"]));
}
