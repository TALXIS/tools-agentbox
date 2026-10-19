using System.Runtime.InteropServices;

namespace TALXIS.AgentBox.CLI.Core.Host;

public enum OsKind
{
    Linux = 0,
    Windows = 1,
    MacOS = 2,
}

/// <summary>
/// Where agentbox runs. A container or ephemeral cloud VM (Codespaces, devcontainers, Claude Code
/// cloud, GitHub Actions/Copilot) may get system-wide installs; a workstation only gets user-scope
/// installs, and system tools are reported with the command to run.
/// </summary>
public enum HostClass
{
    Workstation = 0,
    Container = 1,
}

public interface IHostInfo
{
    OsKind Os { get; }
    HostClass HostClass { get; }
    bool IsRoot { get; }
    string HomeDirectory { get; }
    string? GetEnvironmentVariable(string name);
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

public sealed class SystemHostInfo : IHostInfo
{
    // Each is set to "true" by its environment: GitHub Codespaces, VS Code Dev Containers,
    // Claude Code cloud sessions, GitHub Actions (which also hosts the Copilot coding agent).
    private static readonly string[] ContainerEnvironmentVariables =
        ["CODESPACES", "REMOTE_CONTAINERS", "CLAUDE_CODE_REMOTE", "GITHUB_ACTIONS"];

    private static readonly string[] ContainerMarkerFiles = ["/.dockerenv", "/run/.containerenv"];

    public OsKind Os { get; } =
        OperatingSystem.IsWindows() ? OsKind.Windows
        : OperatingSystem.IsMacOS() ? OsKind.MacOS
        : OsKind.Linux;

    public HostClass HostClass => DetectHostClass();

    public bool IsRoot => Os != OsKind.Windows && GetEuid() == 0;

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    private HostClass DetectHostClass()
    {
        var overridden = GetEnvironmentVariable("AGENTBOX_HOST_CLASS");
        if (Enum.TryParse<HostClass>(overridden, ignoreCase: true, out var explicitClass))
            return explicitClass;
        if (ContainerEnvironmentVariables.Any(v => string.Equals(GetEnvironmentVariable(v), "true", StringComparison.OrdinalIgnoreCase)))
            return HostClass.Container;
        if (Os == OsKind.Linux && ContainerMarkerFiles.Any(File.Exists))
            return HostClass.Container;
        return HostClass.Workstation;
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
