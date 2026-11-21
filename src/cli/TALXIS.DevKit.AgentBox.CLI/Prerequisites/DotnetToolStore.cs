using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

/// <summary>
/// Removes what <c>dotnet tool install</c> leaves in a tool path's <c>.store</c> that the installed
/// tool never reads: the two copies of the package it was extracted from, and the native and
/// platform-specific files for every other operating system and architecture (a tool package
/// carries them all). Applies to any .NET tool AgentBox installs for every user, whichever it is.
/// </summary>
internal static class DotnetToolStore
{
    /// <summary>Never fails the install or update it follows: anything it can't remove stays.</summary>
    public static void Prune(string toolPath, ILogger logger) => Prune(toolPath, RuntimeInformation.RuntimeIdentifier, logger);

    internal static void Prune(string toolPath, string runtimeIdentifier, ILogger logger)
    {
        var store = Path.Combine(toolPath, ".store");
        if (!Directory.Exists(store))
            return;
        long freed = 0;
        try
        {
            // dotnet tool list, update and uninstall go by .nupkg.metadata, which stays.
            foreach (var package in Directory.EnumerateFiles(store, "*.nupkg", SearchOption.AllDirectories))
            {
                freed += new FileInfo(package).Length;
                File.Delete(package);
            }
            if (Kept(runtimeIdentifier) is { } kept)
            {
                foreach (var runtimes in Directory.EnumerateDirectories(store, "runtimes", SearchOption.AllDirectories).ToList())
                {
                    foreach (var platform in Directory.EnumerateDirectories(runtimes).Where(d => !kept.Contains(Path.GetFileName(d))).ToList())
                    {
                        freed += Directory.EnumerateFiles(platform, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                        Directory.Delete(platform, recursive: true);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't remove everything unused from {Store}: {Error}", store, ex.Message);
        }
        if (freed > 0)
            logger.LogDebug("Removed {Megabytes} MB the tools in {Store} don't use", freed / (1024 * 1024), store);
    }

    /// <summary>The runtimes/ folders .NET can load from on this machine (its RID and the ones it
    /// falls back to); null where that isn't known, so nothing there is removed.</summary>
    internal static IReadOnlySet<string>? Kept(string runtimeIdentifier)
    {
        var parts = runtimeIdentifier.Split('-');
        if (parts is not ["linux", .., var architecture])
            return null;
        HashSet<string> kept = [runtimeIdentifier, $"linux-{architecture}", "linux", "unix", "any"];
        if (parts is ["linux", "musl", _])
            kept.Add("linux-musl");
        return kept;
    }
}
