namespace TALXIS.AgentBox.CLI.Host;

/// <summary>
/// Runs agentbox again as root through passwordless sudo, for the part of a command that needs it
/// (installing or updating system-wide tools in a container as a non-root user). Its log goes
/// straight to the terminal; its stdout (the result) comes back.
/// </summary>
public static class Sudo
{
    // sudo resets the environment; these decide how agentbox and the tools it runs behave.
    private static readonly string[] KeptVariables =
    [
        "PATH", "DOTNET_ROOT", "NVM_DIR", "AGENTBOX_CONTAINER", "AGENTBOX_LOG_LEVEL", "AGENTBOX_LOG_FORMAT", "AGENTBOX_FEATURES_FROM",
        "CODESPACES", "REMOTE_CONTAINERS", "CLAUDE_CODE_REMOTE", "GITHUB_ACTIONS",
        "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "https_proxy", "http_proxy", "no_proxy",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "CURL_CA_BUNDLE",
    ];

    public static async Task<bool> IsAvailableAsync(IProcessRunner processes) =>
        processes.FindOnPath("sudo") is not null
        && (await processes.RunAsync("sudo", ["-n", "true"], TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Succeeded;

    public static Task<ProcessResult> RunAgentBoxAsync(IProcessRunner processes, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell which executable is running.");
        List<string> sudoArguments = ["-n", "--preserve-env=" + string.Join(',', KeptVariables), self];
        // Under `dotnet agentbox.dll` (dnx), the process is the dotnet host and argument 0 is the assembly.
        if (Path.GetFileNameWithoutExtension(self) == "dotnet")
            sudoArguments.Add(Environment.GetCommandLineArgs()[0]);
        sudoArguments.AddRange(arguments);
        return processes.RunAsync("sudo", sudoArguments, timeout, cancellationToken: cancellationToken, showErrors: true);
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
