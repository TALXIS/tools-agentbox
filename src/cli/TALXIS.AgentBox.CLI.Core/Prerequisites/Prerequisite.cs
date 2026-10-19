namespace TALXIS.AgentBox.CLI.Core.Prerequisites;

/// <summary>How a prerequisite is installed and kept current.</summary>
public enum PrerequisiteKind
{
    /// <summary>A system-wide tool (SDK, runtime, package-manager install). Root/admin territory.</summary>
    System = 0,
    /// <summary>A .NET global tool (<c>dotnet tool install -g</c>); user scope on every OS.</summary>
    DotnetTool = 1,
    /// <summary>An npm global package; user scope on Windows/macOS.</summary>
    NpmTool = 2,
    /// <summary>A <c>dotnet new</c> template package.</summary>
    DotnetTemplate = 3,
    /// <summary>A tool that updates itself (claude, copilot): present means OK; never updated by agentbox.</summary>
    SelfUpdating = 4,
}

/// <summary>Per-OS manual install commands, shown when agentbox doesn't install the tool itself.</summary>
public sealed record InstallHints(string? Apt = null, string? Winget = null, string? Brew = null);

public sealed record Prerequisite
{
    /// <summary>Stable id used on the command line (<c>agentbox prerequisite install pac</c>).</summary>
    public required string Id { get; init; }

    public required string Description { get; init; }

    /// <summary>Command looked up on PATH to tell whether the tool is present.</summary>
    public required string Command { get; init; }

    public required PrerequisiteKind Kind { get; init; }

    /// <summary>NuGet package id (dotnet tools/templates) or npm package name.</summary>
    public string? Package { get; init; }

    /// <summary>
    /// The devcontainer Feature (path after the registry, e.g. <c>devcontainers/features/node</c>)
    /// that installs this tool in Linux containers. The Features stay the single source of the
    /// Linux install logic, shared with Feature-based devcontainers.
    /// </summary>
    public string? Feature { get; init; }

    /// <summary>
    /// Feature-written marker meaning "follow latest" (<c>/etc/devcontainer-*-cli/auto-update</c>).
    /// A pinned install has no marker, and <c>update</c> leaves it alone.
    /// </summary>
    public string? LinuxAutoUpdateMarker { get; init; }

    /// <summary>
    /// Only needed in Linux containers; workstations (any OS) bring their own equivalent, such as
    /// the OS credential store.
    /// </summary>
    public bool ContainerOnly { get; init; }

    public InstallHints Hints { get; init; } = new();

    /// <summary>Arguments that print the version, for <c>validate --deep</c>.</summary>
    public IReadOnlyList<string> VersionArguments { get; init; } = ["--version"];
}
