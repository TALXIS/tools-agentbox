using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

/// <summary>
/// Opens a browser through <c>playwright-cli</c> and closes it again. Only a real launch covers
/// everything it needs: the browser build its Playwright pins, or the channel or executable it's
/// configured with, the system libraries, and the sandbox when root.
/// </summary>
public sealed class BrowserProbe(IHostInfo host, IProcessRunner processes)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Why the browser didn't open, or null when it did.</summary>
    public async Task<string?> FailureAsync(string command, CancellationToken cancellationToken)
    {
        var session = $"-s=agentbox-check-{Environment.ProcessId}";
        // Runs where it's called from, so a project's .playwright/cli.config.json applies.
        var output = Directory.CreateTempSubdirectory("agentbox-browser-").FullName;
        var environment = new Dictionary<string, string?>
        {
            // It keys its sockets on USER, which shells like docker exec leave unset.
            ["USER"] = host.GetEnvironmentVariable("USER") ?? Environment.UserName,
            ["NO_UPDATE_NOTIFIER"] = "1",
            ["PLAYWRIGHT_MCP_OUTPUT_DIR"] = output, // open saves a snapshot
            ["PLAYWRIGHT_MCP_USER_DATA_DIR"] = null, // a saved profile can be in use by another session
        };
        try
        {
            var open = await processes.RunAsync(command, [session, "open", "about:blank"], Timeout, environment, cancellationToken: cancellationToken).ConfigureAwait(false);
            return open.Succeeded ? null : open.FailureSummary;
        }
        finally
        {
            await processes.RunAsync(command, [session, "close"], Timeout, environment, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Directory.Delete(output, recursive: true);
        }
    }
}
