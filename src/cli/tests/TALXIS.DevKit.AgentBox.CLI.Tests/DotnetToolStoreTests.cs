using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.DevKit.AgentBox.CLI.Prerequisites;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class DotnetToolStoreTests
{
    // The layout dotnet tool install leaves: .store/<id>/<version>/<id>/<version>/...
    private static (string ToolPath, string Package) ToolPathWithOneTool()
    {
        var toolPath = Directory.CreateTempSubdirectory("tool-path-").FullName;
        var package = Path.Combine(toolPath, ".store", "some.tool", "1.0.0", "some.tool", "1.0.0");
        foreach (var file in new[]
        {
            "some.tool.1.0.0.nupkg", "Some.Tool.nupkg", ".nupkg.metadata", "some.tool.1.0.0.nupkg.sha512",
            "tools/net10.0/any/some-tool.dll",
            "tools/net10.0/any/runtimes/linux-x64/native/libthing.so",
            "tools/net10.0/any/runtimes/linux-arm64/native/libthing.so",
            "tools/net10.0/any/runtimes/linux-musl-x64/native/libthing.so",
            "tools/net10.0/any/runtimes/linux/lib/net10.0/Thing.dll",
            "tools/net10.0/any/runtimes/unix/lib/net10.0/Other.dll",
            "tools/net10.0/any/runtimes/unix-x64/native/libother.so",
            "tools/net10.0/any/runtimes/unix-arm64/native/libother.so",
            "tools/net10.0/any/runtimes/win/lib/net10.0/Other.dll",
            "tools/net10.0/any/runtimes/win-x64/native/thing.dll",
            "tools/net10.0/any/runtimes/osx-arm64/native/libthing.dylib",
        })
        {
            var path = Path.Combine(package, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
        return (toolPath, package);
    }

    private static IEnumerable<string> Platforms(string package) =>
        Directory.EnumerateDirectories(Path.Combine(package, "tools", "net10.0", "any", "runtimes")).Select(Path.GetFileName).Order()!;

    [Fact]
    public void Removes_the_package_copies_but_keeps_what_dotnet_tool_reads()
    {
        var (toolPath, package) = ToolPathWithOneTool();

        DotnetToolStore.Prune(toolPath, "linux-x64", NullLogger.Instance);

        Assert.Empty(Directory.EnumerateFiles(toolPath, "*.nupkg", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(package, ".nupkg.metadata")));
        Assert.True(File.Exists(Path.Combine(package, "some.tool.1.0.0.nupkg.sha512")));
        Assert.True(File.Exists(Path.Combine(package, "tools", "net10.0", "any", "some-tool.dll")));
    }

    [Theory]
    [InlineData("linux-x64", new[] { "linux", "linux-x64", "unix", "unix-x64" })]
    [InlineData("linux-arm64", new[] { "linux", "linux-arm64", "unix", "unix-arm64" })]
    [InlineData("linux-musl-x64", new[] { "linux", "linux-musl-x64", "linux-x64", "unix", "unix-x64" })]
    public void Keeps_only_the_platform_folders_this_machine_loads_from(string runtimeIdentifier, string[] expected)
    {
        var (toolPath, package) = ToolPathWithOneTool();

        DotnetToolStore.Prune(toolPath, runtimeIdentifier, NullLogger.Instance);

        Assert.Equal(expected, Platforms(package));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    [InlineData("ubuntu.24.04-x64")] // a distro's own .NET build checks its own RID first
    [InlineData("unknown")]
    public void Leaves_platform_folders_alone_where_it_doesnt_know_the_fallbacks(string runtimeIdentifier)
    {
        var (toolPath, package) = ToolPathWithOneTool();

        DotnetToolStore.Prune(toolPath, runtimeIdentifier, NullLogger.Instance);

        Assert.Equal(10, Platforms(package).Count());
    }

    [Fact]
    public void Does_nothing_for_a_tool_path_without_a_store()
    {
        var toolPath = Directory.CreateTempSubdirectory("tool-path-").FullName;

        DotnetToolStore.Prune(toolPath, "linux-x64", NullLogger.Instance);

        Assert.Empty(Directory.EnumerateFileSystemEntries(toolPath));
    }
}
