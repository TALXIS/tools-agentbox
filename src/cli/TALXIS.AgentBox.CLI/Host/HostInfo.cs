using System.Runtime.InteropServices;

namespace TALXIS.AgentBox.CLI.Host;

public enum OsKind { Linux, Windows, MacOS }

/// <summary>The machine agentbox runs on, behind an interface so tests can describe any host.</summary>
public interface IHostInfo
{
    OsKind Os { get; }

    /// <summary>A container or disposable cloud VM: agentbox may install system-wide there.
    /// Anything else is someone's own machine, where it only installs per user.</summary>
    bool IsContainer { get; }

    bool IsRoot { get; }

    /// <summary>A person at a terminal, who can answer a question.</summary>
    bool IsInteractive { get; }

    string HomeDirectory { get; }

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Where the AgentBox devcontainer Features can be installed.</summary>
    bool IsLinuxContainer => Os == OsKind.Linux && IsContainer;
}

public sealed class SystemHostInfo : IHostInfo
{
    // Each is "true" in its environment: Codespaces, VS Code Dev Containers, Claude Code cloud,
    // GitHub Actions (which also hosts the Copilot cloud agent).
    private static readonly string[] ContainerVariables = ["CODESPACES", "REMOTE_CONTAINERS", "CLAUDE_CODE_REMOTE", "GITHUB_ACTIONS"];

    public OsKind Os { get; } =
        OperatingSystem.IsWindows() ? OsKind.Windows
        : OperatingSystem.IsMacOS() ? OsKind.MacOS
        : OsKind.Linux;

    // AGENTBOX_CONTAINER overrides detection, e.g. for an image build, which has no /.dockerenv.
    public bool IsContainer =>
        bool.TryParse(GetEnvironmentVariable("AGENTBOX_CONTAINER"), out var forced) ? forced
        : ContainerVariables.Any(v => string.Equals(GetEnvironmentVariable(v), "true", StringComparison.OrdinalIgnoreCase))
          || File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");

    public bool IsRoot => Os != OsKind.Windows && GetEuid() == 0;

    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
