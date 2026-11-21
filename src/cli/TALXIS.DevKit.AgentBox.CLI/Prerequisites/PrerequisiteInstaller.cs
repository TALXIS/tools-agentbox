using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Features;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

public enum InstallAction { Present, Installed, Manual, Failed, NotNeeded }

/// <summary><see cref="Detail"/> is the command to run for <see cref="InstallAction.Manual"/>, or why it failed.</summary>
public sealed record InstallResult(string Id, InstallAction Action, string? Detail = null);

/// <summary>
/// Makes prerequisites present in a Linux container: it applies the pinned AgentBox Features (as
/// root, through passwordless sudo if needed) for what's missing or at a version the manifest
/// doesn't allow, and keeps what's already there, whatever brought it.
/// On a person's own machine it changes nothing and reports the command for what's missing.
/// </summary>
public sealed class PrerequisiteInstaller(
    IHostInfo host,
    IProcessRunner processes,
    PrerequisiteInspector inspector,
    IFeatureInstaller? features,
    FeatureLock? featureLock,
    ILogger logger)
{
    private static readonly TimeSpan TemplateInstallTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RootInstallTimeout = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<InstallResult>> InstallAsync(IReadOnlyList<Prerequisite> requested, CancellationToken cancellationToken)
    {
        var targets = WithDependencies(requested);
        var results = new Dictionary<string, InstallResult>(StringComparer.OrdinalIgnoreCase);
        var viaFeatures = new List<Prerequisite>();
        var templates = new List<Prerequisite>();
        foreach (var p in targets)
        {
            var status = (await inspector.InspectAsync(p, checkUpdates: false, cancellationToken).ConfigureAwait(false)).Status;
            if (status == PrerequisiteStatus.NotNeeded)
                results[p.Id] = new(p.Id, InstallAction.NotNeeded);
            // Present at a version the manifest allows: kept, like a package manager keeps an installed
            // package that satisfies its spec. Keeping tools current is prerequisite update's job.
            else if (status == PrerequisiteStatus.Ok)
                results[p.Id] = new(p.Id, InstallAction.Present);
            else if (host.IsLinuxContainer && p.Feature is not null)
                viaFeatures.Add(p);
            else if (host.IsLinuxContainer && p.Kind == PrerequisiteKind.DotnetTemplate)
                templates.Add(p);
            else
                results[p.Id] = new(p.Id, InstallAction.Manual, p.Manual.For(host.Os));
        }

        KeepNothingOnTopOfWhatChanges(targets, results, viaFeatures);
        if (viaFeatures.Count > 0)
        {
            foreach (var result in await InstallViaFeaturesAsync(viaFeatures, cancellationToken).ConfigureAwait(false))
                results[result.Id] = result;
        }
        foreach (var p in templates)
            results[p.Id] = await InstallTemplateAsync(p, cancellationToken).ConfigureAwait(false);

        return targets.Select(t => results[t.Id]).ToList();
    }

    /// <summary>Applying a Feature can take along what was installed on top of it (the node Feature
    /// switches nvm's current version, and a global npm tool under the old one goes with it), so a
    /// present tool whose Feature depends on one being applied is applied again too.</summary>
    private void KeepNothingOnTopOfWhatChanges(IReadOnlyList<Prerequisite> targets, Dictionary<string, InstallResult> results, List<Prerequisite> viaFeatures)
    {
        if (featureLock is null)
            return;
        for (var grew = true; grew;)
        {
            grew = false;
            var applied = viaFeatures.Select(v => v.Feature).ToHashSet();
            foreach (var p in targets.Where(t => t.Feature is not null && results.GetValueOrDefault(t.Id)?.Action == InstallAction.Present))
            {
                var dependsOn = featureLock.Features.FirstOrDefault(f => f.Path == p.Feature)?.DependsOn ?? [];
                if (!dependsOn.Any(applied.Contains))
                    continue;
                results.Remove(p.Id);
                viaFeatures.Add(p);
                grew = true;
            }
        }
    }

    /// <summary>In a container, a prerequisite comes with what its Feature depends on (pa needs the keyring).</summary>
    private IReadOnlyList<Prerequisite> WithDependencies(IReadOnlyList<Prerequisite> requested)
    {
        if (!host.IsLinuxContainer || featureLock is null)
            return requested;
        var needed = requested.Select(p => p.Feature).OfType<string>().ToHashSet();
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var dependency in featureLock.Features.Where(f => needed.Contains(f.Path)).SelectMany(f => f.DependsOn ?? []).ToList())
                grew |= needed.Add(dependency);
        }
        return PrerequisiteCatalog.All.Where(p => requested.Contains(p) || (p.Feature is not null && needed.Contains(p.Feature))).ToList();
    }

    private async Task<IReadOnlyList<InstallResult>> InstallViaFeaturesAsync(IReadOnlyList<Prerequisite> targets, CancellationToken cancellationToken)
    {
        if (!host.IsRoot)
            return await InstallAsRootAsync(targets, cancellationToken).ConfigureAwait(false);
        if (features is null || featureLock is null)
            return Failed(targets, "this build has no Feature lockfile (a development build)");

        var outcomes = await features.InstallAsync(
            featureLock.Features.Where(f => targets.Any(t => t.Feature == f.Path)).ToList(), cancellationToken).ConfigureAwait(false);
        foreach (var p in targets.Where(t => t.Kind == PrerequisiteKind.DotnetTool && t.ToolPath is not null))
            DotnetToolStore.Prune(p.ToolPath!, logger);
        return targets.Select(p => outcomes.FirstOrDefault(o => o.Feature.Path == p.Feature) switch
        {
            null => new InstallResult(p.Id, InstallAction.Failed, "not in this release's Feature lockfile"),
            { Succeeded: true } => new InstallResult(p.Id, InstallAction.Installed),
            var failed => new InstallResult(p.Id, InstallAction.Failed, failed.Error),
        }).ToList();
    }

    private async Task<IReadOnlyList<InstallResult>> InstallAsRootAsync(IReadOnlyList<Prerequisite> targets, CancellationToken cancellationToken)
    {
        if (!await Sudo.IsAvailableAsync(processes).ConfigureAwait(false))
            return Failed(targets, "needs root, and passwordless sudo isn't available");

        logger.LogInformation("Installing {Ids} as root with sudo.", string.Join(", ", targets.Select(t => t.Id)));
        var run = await Sudo.RunAgentBoxAsync(processes, ["prerequisite", "install", .. targets.Select(t => t.Id), "--format", "json"],
            RootInstallTimeout, cancellationToken).ConfigureAwait(false);
        List<InstallResult> elevated;
        try
        {
            elevated = JsonSerializer.Deserialize<List<InstallResult>>(run.StandardOutput, AgentBoxOutputJsonOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            elevated = [];
        }
        var missing = $"the install as root reported nothing ({run.FailureSummary})";
        return targets.Select(t => elevated.FirstOrDefault(r => r.Id == t.Id) ?? new InstallResult(t.Id, InstallAction.Failed, missing)).ToList();
    }

    /// <summary>Templates have no Feature; they install for the current user.</summary>
    private async Task<InstallResult> InstallTemplateAsync(Prerequisite p, CancellationToken cancellationToken)
    {
        if (processes.FindOnPath("dotnet") is null)
            return new(p.Id, InstallAction.Failed, "needs 'dotnet' first");

        logger.LogInformation("Installing {Id}", p.Id);
        var run = await processes.RunAsync("dotnet", ["new", "install", p.Package!], TemplateInstallTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);
        return run.Succeeded ? new(p.Id, InstallAction.Installed) : new(p.Id, InstallAction.Failed, run.FailureSummary);
    }

    private static IReadOnlyList<InstallResult> Failed(IEnumerable<Prerequisite> targets, string reason) =>
        targets.Select(t => new InstallResult(t.Id, InstallAction.Failed, reason)).ToList();
}
