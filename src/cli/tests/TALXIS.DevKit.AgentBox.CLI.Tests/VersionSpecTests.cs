using System.Text;
using NuGet.Versioning;
using TALXIS.DevKit.AgentBox.CLI.Prerequisites;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class VersionSpecTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("lts")]
    [InlineData("os-provided")]
    public void Latest_and_names_allow_any_version(string? spec)
    {
        var parsed = VersionSpec.Parse(spec);

        Assert.True(parsed.IsAny);
        Assert.True(parsed.IsSatisfiedBy(NuGetVersion.Parse("0.1.0-beta")));
    }

    [Theory]
    [InlineData("22", "22.11.0", true)]
    [InlineData("22", "22.0.0", true)]
    [InlineData("22", "23.0.0", false)]
    [InlineData("22", "21.9.9", false)]
    [InlineData("22", "22.12.0-rc.1", false)]
    [InlineData("10.0", "10.0.404", true)]
    [InlineData("10.0", "10.1.100", false)]
    [InlineData("10.0", "11.0.100", false)]
    [InlineData("10.0.x", "10.0.404", true)]
    [InlineData("10.0.*", "10.1.100", false)]
    [InlineData("2.16.1", "2.16.1", true)]
    [InlineData("2.16.1", "2.16.2", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.2", true)]
    [InlineData("2.16.1", "2.16.1-rc.1", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", false)]
    [InlineData("1.0.0-beta.2", "1.0.0", false)]
    public void A_partial_version_floats_and_a_full_one_pins(string spec, string installed, bool satisfied) =>
        Assert.Equal(satisfied, VersionSpec.Parse(spec).IsSatisfiedBy(NuGetVersion.Parse(installed)));

    [Theory]
    [InlineData("22", "22", false)]
    [InlineData("10.0", "10.0", false)]
    [InlineData("10.0.x", "10.0", false)]
    [InlineData("2.16.1", "2.16.1", true)]
    [InlineData("latest", "any", false)]
    public void It_shows_what_was_asked_for(string spec, string shown, bool exact)
    {
        var parsed = VersionSpec.Parse(spec);

        Assert.Equal(shown, parsed.ToString());
        Assert.Equal(exact, parsed.IsExact);
    }
}

public class ToolManifestTests
{
    [Fact]
    public void Each_Features_version_option_is_its_spec()
    {
        var manifest = ToolManifest.Parse(new MemoryStream(Encoding.UTF8.GetBytes("""
            {
                "//": "comment",
                "features": {
                    "ghcr.io/devcontainers/features/node:1": {"version": "22"},
                    "ghcr.io/devcontainers/features/git:1": {},
                    "ghcr.io/talxis/tools-agentbox/txc-cli:1": {"version": "latest"}
                }
            }
            """)));

        Assert.Equal("22", manifest.SpecFor(PrerequisiteCatalog.Select(["node"]).Single()).ToString());
        Assert.True(manifest.SpecFor(PrerequisiteCatalog.Select(["git"]).Single()).IsAny);
        Assert.True(manifest.SpecFor(PrerequisiteCatalog.Select(["txc"]).Single()).IsAny);
        Assert.True(manifest.SpecFor(PrerequisiteCatalog.Select(["devkit-templates"]).Single()).IsAny); // no Feature
    }

    [Fact]
    public void The_build_embeds_the_repository_manifest() =>
        Assert.Equal(
            ToolManifest.Parse(File.OpenRead(Repo.File("src/container/agentbox-features.json"))).Features.Order(),
            ToolManifest.Embedded.Features.Order());
}
