using System.Text.Json;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Prerequisites;
using TALXIS.AgentBox.CLI.Prerequisite;

namespace TALXIS.AgentBox.CLI.Tests;

public class CatalogTests
{
    private const string Manifest = "src/container/templates/power-platform/.devcontainer/devcontainer.features.json";

    // install-features.sh and build/write-feature-lock.sh add this one to the manifest's list.
    private const string AddedDotnetFeature = "devcontainers/features/dotnet";

    [Fact]
    public void Ids_are_unique()
    {
        var ids = PrerequisiteCatalog.All.Select(p => p.Id.ToLowerInvariant()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Catalog_and_Feature_manifest_list_the_same_Features()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Repo.File(Manifest)));
        var inManifest = manifest.RootElement.GetProperty("features").EnumerateObject()
            .Select(f => f.Name)
            .Select(name => name[(name.IndexOf('/') + 1)..])        // drop the registry
            .Select(name => name[..name.LastIndexOf(':')])          // drop the tag
            .Append(AddedDotnetFeature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inCatalog = PrerequisiteCatalog.All.Where(p => p.Feature is not null).Select(p => p.Feature!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(inManifest.Except(inCatalog, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(inCatalog.Except(inManifest, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Packages_are_declared_for_tools_agentbox_installs_by_package()
    {
        foreach (var p in PrerequisiteCatalog.All.Where(p => p.Kind is PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool or PrerequisiteKind.DotnetTemplate))
            Assert.False(string.IsNullOrEmpty(p.Package), p.Id);
    }

    [Fact]
    public void Linux_container_installs_everything()
    {
        var host = new FakeHost { Os = OsKind.Linux, HostClass = HostClass.Container };
        Assert.All(PrerequisiteCatalog.All, p => Assert.True(PrerequisiteListCliCommand.InstalledByAgentBox(host, p), p.Id));
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void Workstations_install_only_user_scope_and_have_a_command_for_the_rest(OsKind os)
    {
        var host = new FakeHost { Os = os, HostClass = HostClass.Workstation, IsRoot = false };
        using var harness = new CommandHarness(host);
        var installer = TALXIS.AgentBox.CLI.Core.AgentBoxServices.Current.Installer(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        foreach (var p in PrerequisiteCatalog.All.Where(p => !p.ContainerOnly))
        {
            var automatic = PrerequisiteListCliCommand.InstalledByAgentBox(host, p);
            Assert.Equal(p.Kind is PrerequisiteKind.DotnetTool or PrerequisiteKind.NpmTool or PrerequisiteKind.DotnetTemplate, automatic);
            if (!automatic)
                Assert.False(string.IsNullOrEmpty(installer.ManualCommand(p)), $"{p.Id} has no manual command on {os}");
        }
    }
}
