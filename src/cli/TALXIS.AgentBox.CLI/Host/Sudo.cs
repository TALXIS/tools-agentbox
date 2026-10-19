using System.Diagnostics;

namespace TALXIS.AgentBox.CLI.Host;

/// <summary>
/// Runs agentbox again as root through passwordless sudo, for the part of a command that needs it
/// (installing system prerequisites in a container as a non-root user). Its logs stream to the
/// terminal; its stdout (the result) is returned.
/// </summary>
public static class Sudo
{
    // sudo resets the environment; these decide how agentbox behaves, so the child must see them.
    private static readonly string[] KeptVariables =
    [
        "PATH", "DOTNET_ROOT", "NVM_DIR", "AGENTBOX_CONTAINER", "AGENTBOX_LOG_LEVEL", "AGENTBOX_LOG_FORMAT",
        "CODESPACES", "REMOTE_CONTAINERS", "CLAUDE_CODE_REMOTE", "GITHUB_ACTIONS", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY",
    ];

    public static async Task<bool> IsAvailableAsync(IProcessRunner processes) =>
        processes.FindOnPath("sudo") is not null
        && (await processes.RunAsync("sudo", ["-n", "true"], TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Succeeded;

    public static async Task<string> RunAgentBoxAsync(IReadOnlyList<string> arguments)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell which executable is running.");
        var startInfo = new ProcessStartInfo("sudo") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-n", "--preserve-env=" + string.Join(',', KeptVariables), self })
            startInfo.ArgumentList.Add(argument);
        // Under `dotnet agentbox.dll` (dnx), the process is the dotnet host and argument 0 is the assembly.
        if (Path.GetFileNameWithoutExtension(self) == "dotnet")
            startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start sudo.");
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return output;
    }

    /// <summary>The user who ran sudo, or the current user, with their home directory.</summary>
    public static (string User, string Home) InvokingUser()
    {
        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (string.IsNullOrEmpty(sudoUser) || sudoUser == "root")
            return (Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var home = File.ReadLines("/etc/passwd").Select(line => line.Split(':')).FirstOrDefault(f => f.Length > 5 && f[0] == sudoUser)?[5];
        return (sudoUser, home ?? Path.Combine("/home", sudoUser));
    }
}
