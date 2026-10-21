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

/// <summary>An SDK version the machine must have, checked by running <see cref="Arguments"/>.</summary>
public sealed record RequiredVersion(IReadOnlyList<string> Arguments, string OutputPattern);

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

    /// <summary>AgentBox installs one version and never changes it.</summary>
    public bool Pinned { get; init; }

    public RequiredVersion? RequiredVersion { get; init; }

    public ManualInstall Manual { get; init; } = new();

    /// <summary>In a container, a present install is kept rather than re-applied from its Feature:
    /// it's managed elsewhere, pinned, or its version was just checked.</summary>
    public bool KeepWhenPresent => Kind == PrerequisiteKind.SelfManaged || Pinned || RequiredVersion is not null;

    /// <summary>This repo's Features leave this file when they follow the latest release
    /// (version "latest"); a pinned install has none, and agentbox doesn't update it.</summary>
    public string? FollowsLatestMarker =>
        Feature is { } feature && feature.StartsWith("talxis/tools-agentbox/", StringComparison.Ordinal)
            ? $"/etc/devcontainer-{feature.Split('/')[^1]}/auto-update"
            : null;
}
