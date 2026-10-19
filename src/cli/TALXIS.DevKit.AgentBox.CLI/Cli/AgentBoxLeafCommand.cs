// Adapted from TALXIS/tools-cli (TxcLeafCommand) so both CLIs share the output and exit-code contract.
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;

namespace TALXIS.DevKit.AgentBox.CLI.Cli;

/// <summary>Base class for every leaf command: <c>--format</c>, exit codes, and error handling.</summary>
public abstract class AgentBoxLeafCommand
{
    protected const int ExitSuccess = 0;
    protected const int ExitError = 1;
    protected const int ExitInvalidInput = 2;
    protected const int ExitUnhealthy = 3;

    [CliOption(Name = "--format", Aliases = ["-f"], Description = "Output format: json (default when piped) or text.", Required = false)]
    public string? Format { get; set; }

    private const string SandboxAdvice =
        "This is your own machine. Coding agents are best run in a sandbox (Codespaces, a devcontainer, a cloud environment): "
        + "isolated from your system, so an incident stays contained, and recreated often, so stray processes and leaks don't build up. "
        + "See https://github.com/TALXIS/tools-agentbox";

    protected abstract ILogger Logger { get; }

    protected abstract Task<int> ExecuteAsync();

    /// <summary>Cancelled when the person presses Ctrl+C.</summary>
    protected CancellationToken Cancellation { get; private set; }

    /// <summary>Called by DotMake. Implement <see cref="ExecuteAsync"/> instead.</summary>
    public Task<int> RunAsync(CliContext context) => InvokeAsync(context.CancellationToken);

    internal async Task<int> InvokeAsync(CancellationToken cancellation = default)
    {
        Cancellation = cancellation;
        OutputContext.Reset();
        if (Format is not null)
        {
            if (!Enum.TryParse<OutputFormat>(Format, ignoreCase: true, out var format))
            {
                Logger.LogError("Unknown output format '{Format}'. Valid values: json, text.", Format);
                return ExitInvalidInput;
            }
            OutputContext.Format = format;
        }

        if (!AgentBoxServices.Current.Host.IsLinuxContainer)
            Logger.LogWarning(SandboxAdvice);

        try
        {
            return await ExecuteAsync().ConfigureAwait(false);
        }
        catch (InvalidInputException ex)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitInvalidInput;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.LogWarning("Cancelled.");
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Error}", ex.Message);
            return ExitError;
        }
    }
}
