using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Features;
using TALXIS.AgentBox.CLI.Harness;
using TALXIS.AgentBox.CLI.Host;
using TALXIS.AgentBox.CLI.Prerequisites;
using TALXIS.AgentBox.CLI.Registry;

namespace TALXIS.AgentBox.CLI;

/// <summary>
/// Wires the commands to the real machine. DotMake creates the command objects itself, so they
/// reach their dependencies through <see cref="Current"/>, which tests replace with fakes.
/// </summary>
public sealed class AgentBoxServices(IHostInfo host, IProcessRunner processes, IPackageRegistryClient registry, IPrompt prompt, IFeatureInstaller? features, FeatureLock? featureLock)
{
    public static AgentBoxServices Current { get; set; } = new(
        new SystemHostInfo(),
        new ProcessRunner(),
        new PackageRegistryClient(Http.Shared),
        new ConsolePrompt(),
        OperatingSystem.IsLinux() ? new FeatureInstaller(new ProcessRunner(), Http.Shared, AgentBoxLoggerFactory.CreateLogger("features")) : null,
        FeatureLock.LoadEmbedded());

    public IHostInfo Host => host;

    public PrerequisiteInspector Inspector() => new(host, processes, registry, new StateStore(host));

    public PrerequisiteInstaller Installer(ILogger logger) => new(host, processes, Inspector(), new StateStore(host), features, featureLock, logger);

    public PrerequisiteUpdater Updater(ILogger logger) => new(host, processes, Inspector(), new StateStore(host), logger);

    public HarnessService Harness(ILogger logger) => new(host, processes, prompt, logger);
}
