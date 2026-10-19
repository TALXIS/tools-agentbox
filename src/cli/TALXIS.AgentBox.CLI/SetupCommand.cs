using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Harness;
using TALXIS.AgentBox.CLI.Prerequisites;

namespace TALXIS.AgentBox.CLI;

[CliCommand(Name = "setup", Description = "Make this sandbox ready in one go: install the prerequisites, set up the installed harnesses, then check. For Linux containers and VMs dedicated to agents. Exits with 3 when something is still missing.")]
public class SetupCommand : AgentBoxLeafCommand
{
    protected override ILogger Logger { get; } = AgentBoxLoggerFactory.CreateLogger("setup");

    [CliOption(Name = "--no-harness", Description = "Skip the harness setup (an image build: the container does it when it's created).", Required = false)]
    public bool NoHarness { get; set; }

    public sealed record Result(IReadOnlyList<InstallResult> Install, IReadOnlyList<ConfigureResult> Harness, IReadOnlyList<PrerequisiteState> Validate);

    protected override async Task<int> ExecuteAsync()
    {
        var services = AgentBoxServices.Current;
        if (!services.Host.IsLinuxContainer)
            throw new ArgumentException("setup is for Linux containers and VMs dedicated to agents (AGENTBOX_CONTAINER=true marks one that isn't detected). On your own machine, run 'prerequisite validate'.");

        var install = await services.Installer(Logger).InstallAsync(PrerequisiteCatalog.All, CancellationToken.None).ConfigureAwait(false);
        var harness = new List<ConfigureResult>();
        if (!NoHarness)
        {
            var harnesses = services.Harness(Logger);
            foreach (var kind in harnesses.Select(null))
                harness.Add(await harnesses.ConfigureAsync(kind).ConfigureAwait(false));
        }
        var inspector = services.Inspector();
        var validate = await Task.WhenAll(PrerequisiteCatalog.All.Select(p => inspector.InspectAsync(p, checkUpdates: false, CancellationToken.None))).ConfigureAwait(false);

        OutputFormatter.Write(new Result(install, harness, validate), r =>
        {
            OutputWriter.WriteLine("install");
            PrerequisiteInstallCommand.Render(r.Install);
            if (r.Harness.Count > 0)
            {
                OutputWriter.WriteLine("harness");
                HarnessConfigureCommand.Render(r.Harness);
            }
            OutputWriter.WriteLine("validate");
            PrerequisiteValidateCommand.Render(r.Validate);
        });
        if (validate.Any(v => v.Status == PrerequisiteStatus.Missing))
            return ExitUnhealthy;
        return install.Any(i => i.Action == InstallAction.Failed) || harness.Any(h => h.Status == ConfigureStatus.Failed) ? ExitError : ExitSuccess;
    }
}
