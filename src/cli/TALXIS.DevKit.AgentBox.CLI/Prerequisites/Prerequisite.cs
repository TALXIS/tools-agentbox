using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

public enum PrerequisiteKind
{
    /// <summary>System-wide (SDK, runtime, OS package).</summary>
    System,
    /// <summary>A .NET global tool.</summary>
    DotnetTool,
    /// <summary>A global npm package.</summary>
    NpmTool,
    /// <summary>A <c>dotnet new</c> template package.</summary>
    DotnetTemplate,
    /// <summary>Kept current by something else (Claude Code and Copilot update themselves; agentbox
    /// comes with the image or dnx): agentbox only makes sure it's there.</summary>
    SelfManaged,
    /// <summary>A browser <c>playwright-cli</c> can launch, whichever it's configured to use.</summary>
    PlaywrightBrowser,
}

/// <summary>How to install a prerequisite by hand, per OS, on a machine agentbox doesn't install on.</summary>
public sealed record ManualInstall(string? Linux = null, string? Windows = null, string? MacOS = null)
{
    public static ManualInstall Everywhere(string command) => new(command, command, command);

    public string? For(OsKind os) => os switch
    {
        OsKind.Windows => Windows,
        OsKind.MacOS => MacOS,
        _ => Linux,
    };
}

/// <summary>How to read the installed version(s) of a tool whose version isn't on disk where agentbox
/// can find it: run <see cref="Arguments"/>, and every match of <see cref="OutputPattern"/>'s first
/// group is an installed version (an SDK can have several).</summary>
public sealed record VersionProbe(IReadOnlyList<string> Arguments, string OutputPattern);

public sealed record Prerequisite
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    /// <summary>The command whose presence on PATH means it's installed.</summary>
    public required string Command { get; init; }

    public required PrerequisiteKind Kind { get; init; }

    /// <summary>NuGet package id (dotnet tools and templates) or npm package name.</summary>
    public string? Package { get; init; }

    /// <summary>The devcontainer Feature (registry path, e.g. <c>devcontainers/features/node</c>)
    /// that installs it in Linux containers.</summary>
    public string? Feature { get; init; }

    /// <summary>Where the Feature installs an npm package, if not npm's global prefix.</summary>
    public string? NpmPrefix { get; init; }

    /// <summary>Where the Feature installs a .NET tool for every user (<c>--tool-path</c>). Installs
    /// from before that are the user's own global ones, in ~/.dotnet/tools.</summary>
    public string? ToolPath { get; init; }

    /// <summary>Only needed in Linux containers; machines bring their own equivalent.</summary>
    public bool ContainerOnly { get; init; }

    /// <summary>For a System tool, how to read its version, so a manifest version other than
    /// "latest" can be checked. .NET and npm tools are read from disk instead.</summary>
    public VersionProbe? VersionProbe { get; init; }

    public ManualInstall Manual { get; init; } = new();

    /// <summary>This repo's Features leave this file when they follow the latest release
    /// (version "latest"); a pinned install has none, and agentbox doesn't update it.</summary>
    public string? FollowsLatestMarker =>
        Feature is { } feature && feature.StartsWith("talxis/tools-agentbox/", StringComparison.Ordinal)
            ? $"/etc/devcontainer-{feature.Split('/')[^1]}/auto-update"
            : null;
}
