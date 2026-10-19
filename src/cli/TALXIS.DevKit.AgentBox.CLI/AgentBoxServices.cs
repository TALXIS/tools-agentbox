using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Features;
using TALXIS.DevKit.AgentBox.CLI.Harness;
using TALXIS.DevKit.AgentBox.CLI.Host;
using TALXIS.DevKit.AgentBox.CLI.Prerequisites;
using TALXIS.DevKit.AgentBox.CLI.Registry;

namespace TALXIS.DevKit.AgentBox.CLI;

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

    public PrerequisiteInspector Inspector() => new(host, processes, registry);

    public PrerequisiteInstaller Installer(ILogger logger) => new(host, processes, Inspector(), features, featureLock, logger);

    public PrerequisiteUpdater Updater(ILogger logger) => new(host, processes, Inspector(), logger);

    public HarnessService Harness(ILogger logger) => new(host, processes, prompt, logger);
}
