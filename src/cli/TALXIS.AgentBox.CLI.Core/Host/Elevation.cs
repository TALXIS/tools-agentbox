using System.Diagnostics;

namespace TALXIS.AgentBox.CLI.Core.Host;

/// <summary>
/// Installing through Features in a Linux container needs root. When agentbox runs as another user
/// who has passwordless sudo (the usual devcontainer setup), just that part runs as a second
/// agentbox under <c>sudo -n</c>; everything user-scope stays with the user.
/// </summary>
public static class Elevation
{
    /// <summary>Set on the elevated child so it never tries again.</summary>
    public const string ElevatedVariable = "AGENTBOX_ELEVATED";

    // Detection and logging settings the elevated run must see; sudo resets everything else.
    private static readonly string[] PreservedVariables =
    [
        "PATH", "DOTNET_ROOT", "NVM_DIR", "AGENTBOX_HOST_CLASS", "AGENTBOX_HARNESS", "AGENTBOX_LOG_LEVEL", "AGENTBOX_LOG_FORMAT",
        "CODESPACES", "REMOTE_CONTAINERS", "CLAUDE_CODE_REMOTE", "GITHUB_ACTIONS", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY",
    ];

    public static bool Needed(IHostInfo host) =>
        host.Os == OsKind.Linux && host.HostClass == HostClass.Container && !host.IsRoot
        && host.GetEnvironmentVariable(ElevatedVariable) is null;

    /// <summary>Whether <c>sudo -n</c> works without a password prompt.</summary>
    public static async Task<bool> CanSudoAsync(IProcessRunner processes, CancellationToken cancellationToken) =>
        processes.FindOnPath("sudo") is not null
        && (await processes.RunAsync("sudo", ["-n", "true"], TimeSpan.FromSeconds(10), cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;

    /// <summary>
    /// Runs agentbox with <paramref name="arguments"/> as root. Its logs (stderr) go straight to the
    /// terminal; its results (stdout) are returned for the caller to merge into its own output.
    /// Works both for the installed tool and for <c>dnx</c>/<c>dotnet agentbox.dll</c> runs.
    /// </summary>
    public static async Task<(int ExitCode, string StandardOutput)> RunAsRootAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell which executable is running.");
        var startInfo = new ProcessStartInfo("sudo") { UseShellExecute = false, RedirectStandardOutput = true };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("--preserve-env=" + string.Join(',', PreservedVariables));
        startInfo.ArgumentList.Add("env");
        startInfo.ArgumentList.Add(ElevatedVariable + "=1");
        startInfo.ArgumentList.Add(processPath);
        // Under the dotnet host, argument 0 is the agentbox assembly; under the apphost it's the apphost itself.
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start sudo.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, output);
    }

    /// <summary>The user who ran sudo (when elevated), otherwise the current user, with their home directory.</summary>
    public static (string User, string Home) InvokingUser()
    {
        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (string.IsNullOrEmpty(sudoUser) || sudoUser == "root")
            return (Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var home = File.Exists("/etc/passwd")
            ? File.ReadLines("/etc/passwd").Select(l => l.Split(':')).FirstOrDefault(f => f.Length > 5 && f[0] == sudoUser)?[5]
            : null;
        return (sudoUser, home ?? Path.Combine("/home", sudoUser));
    }
}
