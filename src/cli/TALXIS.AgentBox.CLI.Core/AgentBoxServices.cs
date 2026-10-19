using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core.Features;
using TALXIS.AgentBox.CLI.Core.Harness;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Prerequisites;
using TALXIS.AgentBox.CLI.Core.Registry;

namespace TALXIS.AgentBox.CLI.Core;

/// <summary>
/// Composition root for the command classes. Tests replace <see cref="Current"/> with fakes for
/// the host, process runner and registries.
/// </summary>
public sealed class AgentBoxServices(IHostInfo host, IProcessRunner processes, IPackageRegistryClient registry, Func<FeatureLock?> featureLock)
{
    public static AgentBoxServices Current { get; set; } = CreateDefault();

    public IHostInfo Host { get; } = host;
    public IProcessRunner Processes { get; } = processes;
    public IPackageRegistryClient Registry { get; } = registry;
    public StateStore State { get; } = new(host);

    public static AgentBoxServices CreateDefault() =>
        new(new SystemHostInfo(), new ProcessRunner(), PackageRegistryClient.Shared, FeatureLock.LoadEmbedded);

    public PrerequisiteInspector Inspector() => new(Host, Processes, Registry, State);

    public PrerequisiteInstaller Installer(ILogger logger) => new(Host, Processes, Inspector(), State, featureLock, RunFeaturesAsync(logger), logger);

    public PrerequisiteUpdater Updater(ILogger logger) => new(Host, Processes, Inspector(), State, logger);

    public HarnessService Harness(ILogger logger) => new(Host, Processes, logger);

    private Func<IReadOnlyList<LockedFeature>, CancellationToken, Task<IReadOnlyList<(LockedFeature Feature, bool Succeeded, string? Error)>>> RunFeaturesAsync(ILogger logger) =>
        (features, cancellationToken) => OperatingSystem.IsLinux()
            ? new FeatureInstaller(Processes, SharedHttp.Client, logger).InstallAsync(features, cancellationToken)
            : throw new PlatformNotSupportedException("Features install only on Linux.");
}

internal static class SharedHttp
{
#pragma warning disable RS0030 // The one shared instance for registry/OCI downloads
    public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
#pragma warning restore RS0030
}
