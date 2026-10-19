using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Prerequisites;

namespace TALXIS.AgentBox.CLI.Prerequisite;

[CliCommand(
    Name = "prerequisite",
    Alias = "prereq",
    Description = "List, check, install and update the prerequisites for working with AgentBox.",
    Children = new[] { typeof(PrerequisiteListCliCommand), typeof(PrerequisiteValidateCliCommand), typeof(PrerequisiteInstallCliCommand), typeof(PrerequisiteUpdateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PrerequisiteCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();

    /// <summary>The named prerequisites (catalog order), or all of them; unknown ids are input errors.</summary>
    internal static IReadOnlyList<Core.Prerequisites.Prerequisite> Select(IReadOnlyCollection<string>? ids)
    {
        if (ids is null || ids.Count == 0)
            return PrerequisiteCatalog.All;
        var unknown = ids.Where(id => PrerequisiteCatalog.Find(id) is null).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown prerequisite(s): {string.Join(", ", unknown)}. See 'agentbox prerequisite list'.");
        return PrerequisiteCatalog.All.Where(p => ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Installs <paramref name="targets"/>. When the Feature installs need root and passwordless
    /// sudo works, those run in an elevated agentbox and the rest here, as the user, afterwards
    /// (so user-scope prerequisites land in the user's home, not root's).
    /// </summary>
    internal static async Task<IReadOnlyList<InstallResult>> InstallAsync(AgentBoxServices services, IReadOnlyList<Core.Prerequisites.Prerequisite> targets, bool reinstall, ILogger logger)
    {
        var installer = services.Installer(logger);
        if (!Elevation.Needed(services.Host)
            || !await installer.NeedsRootAsync(targets, reinstall, CancellationToken.None).ConfigureAwait(false)
            || !await Elevation.CanSudoAsync(services.Processes, CancellationToken.None).ConfigureAwait(false))
            return await installer.InstallAsync(targets, reinstall, CancellationToken.None).ConfigureAwait(false);

        var viaFeatures = targets.Where(t => t.Feature is not null).ToList();
        logger.LogInformation("Installing {Count} prerequisite(s) as root with sudo.", viaFeatures.Count);
        var arguments = new List<string> { "prerequisite", "install" };
        arguments.AddRange(viaFeatures.Select(t => t.Id));
        if (reinstall)
            arguments.Add("--reinstall");
        arguments.AddRange(["--format", "json"]);
        var (_, output) = await Elevation.RunAsRootAsync(arguments, CancellationToken.None).ConfigureAwait(false);

        List<InstallResult> elevated;
        try
        {
            elevated = JsonSerializer.Deserialize<List<InstallResult>>(output, AgentBoxOutputJsonOptions.Default) ?? [];
        }
        catch (JsonException)
        {
            elevated = [];
        }
        var rest = await installer.InstallAsync(targets.Where(t => t.Feature is null).ToList(), reinstall, CancellationToken.None).ConfigureAwait(false);
        var all = elevated.Concat(rest).ToList();
        return targets
            .Select(t => all.FirstOrDefault(r => string.Equals(r.Id, t.Id, StringComparison.OrdinalIgnoreCase))
                ?? new InstallResult(t.Id, InstallAction.Failed, "the elevated install reported nothing"))
            .ToList();
    }

    internal static string HostDescription(IHostInfo host) =>
        $"{host.Os.ToString().ToLowerInvariant()}, {host.HostClass.ToString().ToLowerInvariant()}{(host.IsRoot ? ", root" : "")}";
}
