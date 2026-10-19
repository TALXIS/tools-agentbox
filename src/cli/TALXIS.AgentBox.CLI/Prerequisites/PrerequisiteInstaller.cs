using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Features;
using TALXIS.AgentBox.CLI.Host;

namespace TALXIS.AgentBox.CLI.Prerequisites;

public enum InstallAction { Present, Installed, Manual, Failed, NotNeeded }

/// <summary><see cref="Detail"/> is the command to run for <see cref="InstallAction.Manual"/>, or why it failed.</summary>
public sealed record InstallResult(string Id, InstallAction Action, string? Detail = null);

/// <summary>
/// Makes prerequisites present in a Linux container: it applies the pinned AgentBox Features (as
/// root, through passwordless sudo if needed), so every container ends up with the same tool set
/// whatever the base image brought; only tools that update themselves are left alone when present.
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
            else if (host.IsLinuxContainer && p.Feature is not null && !(p.Kind == PrerequisiteKind.SelfUpdating && status == PrerequisiteStatus.Ok))
                viaFeatures.Add(p);
            else if (status == PrerequisiteStatus.Ok)
                results[p.Id] = new(p.Id, InstallAction.Present);
            else if (host.IsLinuxContainer && p.Kind == PrerequisiteKind.DotnetTemplate)
                templates.Add(p);
            else
                results[p.Id] = new(p.Id, InstallAction.Manual, p.Manual.For(host.Os));
        }

        if (viaFeatures.Count > 0)
        {
            foreach (var result in await InstallViaFeaturesAsync(viaFeatures, cancellationToken).ConfigureAwait(false))
                results[result.Id] = result;
        }
        foreach (var p in templates)
            results[p.Id] = await InstallTemplateAsync(p, cancellationToken).ConfigureAwait(false);

        return targets.Select(t => results[t.Id]).ToList();
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
            return await InstallAsRootAsync(targets).ConfigureAwait(false);
        if (features is null || featureLock is null)
            return Failed(targets, "this build has no Feature lockfile (a development build)");

        var outcomes = await features.InstallAsync(
            featureLock.Features.Where(f => targets.Any(t => t.Feature == f.Path)).ToList(), cancellationToken).ConfigureAwait(false);
        return targets.Select(p => outcomes.FirstOrDefault(o => o.Feature.Path == p.Feature) switch
        {
            null => new InstallResult(p.Id, InstallAction.Failed, "not in this release's Feature lockfile"),
            { Succeeded: true } => new InstallResult(p.Id, InstallAction.Installed),
            var failed => new InstallResult(p.Id, InstallAction.Failed, failed.Error),
        }).ToList();
    }

    private async Task<IReadOnlyList<InstallResult>> InstallAsRootAsync(IReadOnlyList<Prerequisite> targets)
    {
        if (!await Sudo.IsAvailableAsync(processes).ConfigureAwait(false))
            return Failed(targets, "needs root, and passwordless sudo isn't available");

        logger.LogInformation("Installing {Ids} as root with sudo.", string.Join(", ", targets.Select(t => t.Id)));
        var output = await Sudo.RunAgentBoxAsync(["prerequisite", "install", .. targets.Select(t => t.Id), "--format", "json"]).ConfigureAwait(false);
        List<InstallResult> elevated;
        try
        {
            elevated = JsonSerializer.Deserialize<List<InstallResult>>(output, AgentBoxOutputJsonOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            elevated = [];
        }
        return targets
            .Select(t => elevated.FirstOrDefault(r => r.Id == t.Id) ?? new InstallResult(t.Id, InstallAction.Failed, "the install as root reported nothing"))
            .ToList();
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
