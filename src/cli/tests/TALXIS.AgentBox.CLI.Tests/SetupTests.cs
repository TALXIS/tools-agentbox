using System.Text.Json;

namespace TALXIS.AgentBox.CLI.Tests;

public class SetupTests
{
    private static FakeHost WithTemplates()
    {
        var host = new FakeHost();
        var packages = Directory.CreateDirectory(Path.Combine(host.HomeDirectory, ".templateengine", "packages"));
        File.WriteAllText(Path.Combine(packages.FullName, "TALXIS.DevKit.Templates.Dataverse.1.0.0.nupkg"), "");
        return host;
    }

    [Fact]
    public async Task Refuses_on_a_persons_own_machine()
    {
        using var harness = new CommandHarness(new FakeHost { IsContainer = false });
        Assert.Equal(2, await new SetupCommand { Format = "json" }.RunAsync());
    }

    [Fact]
    public async Task Installs_sets_up_the_harnesses_then_validates()
    {
        // Without root or sudo the Feature installs fail; validate still sees everything.
        using var harness = new CommandHarness(WithTemplates(), FakeProcesses.WithEverything());
        Assert.Equal(1, await new SetupCommand { Format = "json" }.RunAsync());
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal(["claude", "copilot"], json.RootElement.GetProperty("harness").EnumerateArray().Select(h => h.GetProperty("harness").GetString()));
        Assert.All(json.RootElement.GetProperty("validate").EnumerateArray(), v => Assert.Equal("ok", v.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task No_harness_skips_the_harnesses_and_missing_tools_exit_3()
    {
        var processes = FakeProcesses.WithEverything();
        processes.OnPath.Remove("az");
        using var harness = new CommandHarness(WithTemplates(), processes);
        Assert.Equal(3, await new SetupCommand { Format = "json", NoHarness = true }.RunAsync());
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal(0, json.RootElement.GetProperty("harness").GetArrayLength());
    }
}
