using TALXIS.DevKit.AgentBox.CLI.Prerequisites;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class PlaywrightTests
{
    private static Prerequisite Row(string id) => PrerequisiteCatalog.Select([id]).Single();

    private static Task<PrerequisiteState> InspectAsync(FakeProcesses processes, string id)
    {
        using var _ = new CommandHarness(new FakeHost(), processes);
        return AgentBoxServices.Current.Inspector().InspectAsync(Row(id), checkUpdates: false, CancellationToken.None);
    }

    [Fact]
    public async Task Without_the_CLI_the_browser_is_missing_and_nothing_runs()
    {
        var processes = new FakeProcesses();

        var state = await InspectAsync(processes, "browser");

        Assert.Equal(PrerequisiteStatus.Missing, state.Status);
        Assert.Equal("playwright-cli not found", state.Detail);
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task The_browser_is_there_when_the_CLI_opens_it()
    {
        var processes = new FakeProcesses { OnPath = { ["playwright-cli"] = "/usr/local/bin/playwright-cli" } };

        var state = await InspectAsync(processes, "browser");

        Assert.Equal(PrerequisiteStatus.Ok, state.Status);
        Assert.Collection(processes.Calls,
            open => Assert.EndsWith(" open about:blank", open),
            close => Assert.EndsWith(" close", close));
    }

    [Fact]
    public async Task A_browser_that_does_not_open_is_missing_with_the_reason_and_is_still_closed()
    {
        var processes = new FakeProcesses
        {
            OnPath = { ["playwright-cli"] = "/usr/local/bin/playwright-cli" },
            Handler = (_, args) => args.Contains("open")
                ? new(1, "", "Error: Daemon pid=1: Daemon process exited with code 1\nError: Browser \"chrome\" is not installed\n    at launch (core.js:1:1)\n", false)
                : new(0, "", "", false),
        };

        var state = await InspectAsync(processes, "browser");

        Assert.Equal(PrerequisiteStatus.Missing, state.Status);
        Assert.Equal("Error: Browser \"chrome\" is not installed", state.Detail);
        Assert.EndsWith(" close", processes.Calls.Last());
    }

    [Fact]
    public async Task A_plain_global_npm_install_reports_its_version()
    {
        var npm = Directory.CreateTempSubdirectory("npm-global-").FullName;
        var package = Path.Combine(npm, "lib", "node_modules", "@playwright", "cli");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "package.json"), """{"name": "@playwright/cli", "version": "0.1.22"}""");
        File.WriteAllText(Path.Combine(package, "playwright-cli.js"), "");
        Directory.CreateDirectory(Path.Combine(npm, "bin"));
        var command = Path.Combine(npm, "bin", "playwright-cli");
        File.CreateSymbolicLink(command, "../lib/node_modules/@playwright/cli/playwright-cli.js");
        var processes = new FakeProcesses { OnPath = { ["playwright-cli"] = command } };

        var state = await InspectAsync(processes, "playwright-cli");

        Assert.Equal(PrerequisiteStatus.Ok, state.Status);
        Assert.Equal("0.1.22", state.InstalledVersion);
    }
}
