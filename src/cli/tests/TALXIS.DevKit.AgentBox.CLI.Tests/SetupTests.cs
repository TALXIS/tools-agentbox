using System.Text.Json;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class SetupTests
{
    [Fact]
    public async Task Refuses_on_a_persons_own_machine()
    {
        using var harness = new CommandHarness(new FakeHost { IsContainer = false });
        Assert.Equal(2, await new SetupCommand { Format = "json" }.InvokeAsync());
    }

    [Fact]
    public async Task No_harness_skips_the_harnesses_and_missing_tools_exit_3()
    {
        var processes = FakeProcesses.WithEverything();
        processes.OnPath.Remove("az");
        using var harness = new CommandHarness(new FakeHost(), processes);
        Assert.Equal(3, await new SetupCommand { Format = "json", NoHarness = true }.InvokeAsync());
        using var json = JsonDocument.Parse(harness.Output);
        Assert.Equal(0, json.RootElement.GetProperty("harness").GetArrayLength());
    }
}
