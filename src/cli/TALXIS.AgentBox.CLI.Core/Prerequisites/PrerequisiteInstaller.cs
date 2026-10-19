using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core.Features;
using TALXIS.AgentBox.CLI.Core.Host;

namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

public enum InstallAction
{
    AlreadyPresent = 0,
    Installed = 1,
    Manual = 2,
    Failed = 3,
    NotApplicable = 4,
}

public sealed record InstallResult(string Id, InstallAction Action, string? Detail = null, string? Command = null);

/// <summary>
/// <c>agentbox prerequisite install</c>: installs what's missing, never updates (that's
/// <see cref="PrerequisiteUpdater"/>). What it may install depends on where it runs:
/// <list type="bullet">
///   <item>Linux container/ephemeral VM, as root: everything, through the locked devcontainer Features.</item>
///   <item>Anywhere: user-scope tools (dotnet global tools, npm tools on Windows/macOS, templates).</item>
///   <item>Workstations: system tools are reported with the exact command to run, not installed.</item>
/// </list>
/// </summary>
public sealed class PrerequisiteInstaller(
    IHostInfo host,
    IProcessRunner processes,
    PrerequisiteInspector inspector,
    StateStore state,
    Func<FeatureLock?> featureLock,
    Func<IReadOnlyList<LockedFeature>, CancellationToken, Task<IReadOnlyList<(LockedFeature Feature, bool Succeeded, string? Error)>>> runFeatures,
    ILogger logger)
{
    private static readonly TimeSpan ToolInstallTimeout = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<InstallResult>> InstallAsync(IReadOnlyList<Prerequisite> targets, bool reinstall, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, InstallResult>(StringComparer.OrdinalIgnoreCase);
        var todo = new List<Prerequisite>();
        foreach (var prerequisite in targets)
        {
            var current = await inspector.InspectAsync(prerequisite, new InspectOptions(), cancellationToken).ConfigureAwait(false);
            if (current.Status == PrerequisiteStatus.NotApplicable)
                results[prerequisite.Id] = new(prerequisite.Id, InstallAction.NotApplicable, current.Detail);
            else if (current.Status != PrerequisiteStatus.Missing && !reinstall)
                results[prerequisite.Id] = new(prerequisite.Id, InstallAction.AlreadyPresent, current.InstalledVersion);
            else
                todo.Add(prerequisite);
        }

        // Linux containers: the Features install everything, in the locked dependency order.
        if (host.Os == OsKind.Linux && host.HostClass == HostClass.Container && todo.Any(p => p.Feature is not null))
        {
            var viaFeatures = todo.Where(p => p.Feature is not null).ToList();
            var locked = featureLock();
            if (!host.IsRoot)
            {
                foreach (var p in viaFeatures)
                    results[p.Id] = new(p.Id, InstallAction.Failed, "needs root, and passwordless sudo isn't available; run agentbox as root");
            }
            else if (locked is null)
            {
                foreach (var p in viaFeatures)
                    results[p.Id] = new(p.Id, InstallAction.Failed, "this agentbox build has no Feature lockfile (dev build); use a released version");
            }
            else
            {
                var wanted = viaFeatures.Select(p => p.Feature!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var features = locked.Features.Where(f => wanted.Contains(f.Path)).ToList();
                var outcomes = await runFeatures(features, cancellationToken).ConfigureAwait(false);
                foreach (var p in viaFeatures)
                {
                    var outcome = outcomes.FirstOrDefault(o => string.Equals(o.Feature.Path, p.Feature, StringComparison.OrdinalIgnoreCase));
                    results[p.Id] = outcome.Feature is null
                        ? new(p.Id, InstallAction.Failed, $"Feature {p.Feature} is not in this release's lockfile")
                        : outcome.Succeeded
                            ? new(p.Id, InstallAction.Installed)
                            : new(p.Id, InstallAction.Failed, outcome.Error);
                    if (outcome.Succeeded)
                        state.RecordInstalled(p.Id, version: null, followsLatest: p.LinuxAutoUpdateMarker is not null);
                }
            }
            todo = todo.Where(p => p.Feature is null).ToList();
        }

        foreach (var prerequisite in todo)
            results[prerequisite.Id] = await InstallOneAsync(prerequisite, reinstall, cancellationToken).ConfigureAwait(false);

        return targets.Select(t => results[t.Id]).ToList();
    }

    /// <summary>
    /// Whether installing <paramref name="targets"/> here needs root: in a Linux container, a
    /// prerequisite that would be installed through its Feature.
    /// </summary>
    public async Task<bool> NeedsRootAsync(IReadOnlyList<Prerequisite> targets, bool reinstall, CancellationToken cancellationToken)
    {
        if (host.Os != OsKind.Linux || host.HostClass != HostClass.Container || host.IsRoot)
            return false;
        foreach (var prerequisite in targets.Where(t => t.Feature is not null))
        {
            var current = await inspector.InspectAsync(prerequisite, new InspectOptions(), cancellationToken).ConfigureAwait(false);
            if (current.Status != PrerequisiteStatus.NotApplicable && (reinstall || current.Status == PrerequisiteStatus.Missing))
                return true;
        }
        return false;
    }

    private async Task<InstallResult> InstallOneAsync(Prerequisite p, bool reinstall, CancellationToken cancellationToken)
    {
        (string File, string[] Args)? command = p.Kind switch
        {
            PrerequisiteKind.DotnetTool => ("dotnet", ["tool", reinstall ? "update" : "install", "--global", p.Package!]),
            PrerequisiteKind.NpmTool => ("npm", ["install", "--global", p.Package + "@latest"]),
            PrerequisiteKind.DotnetTemplate => ("dotnet", ["new", "install", p.Package!]),
            _ => null,
        };
        if (command is null)
            return new(p.Id, InstallAction.Manual, "install it with the command below", ManualCommand(p));

        var (file, args) = command.Value;
        if (processes.FindOnPath(file) is null)
            return new(p.Id, InstallAction.Failed, $"needs '{file}' on PATH first");

        logger.LogInformation("Installing {Id}: {File} {Args}", p.Id, file, string.Join(' ', args));
        var run = await processes.RunAsync(file, args, ToolInstallTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!run.Succeeded)
            return new(p.Id, InstallAction.Failed, FirstLine(run.StandardError) ?? FirstLine(run.StandardOutput) ?? $"exit code {run.ExitCode}");

        state.RecordInstalled(p.Id, version: null, followsLatest: true);
        return new(p.Id, InstallAction.Installed);
    }

    public string? ManualCommand(Prerequisite p) => host.Os switch
    {
        OsKind.Windows => p.Hints.Winget,
        OsKind.MacOS => p.Hints.Brew,
        _ => p.Hints.Apt,
    };

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
