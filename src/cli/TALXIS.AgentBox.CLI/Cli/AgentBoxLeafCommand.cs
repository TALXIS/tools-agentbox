// Adapted from TALXIS/tools-cli (TxcLeafCommand) so both CLIs share the output and exit-code contract.
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;

namespace TALXIS.AgentBox.CLI.Cli;

/// <summary>Base class for every leaf command: <c>--format</c>, exit codes, and error handling.</summary>
public abstract class AgentBoxLeafCommand
{
    protected const int ExitSuccess = 0;
    protected const int ExitError = 1;
    protected const int ExitInvalidInput = 2;
    protected const int ExitUnhealthy = 3;

    [CliOption(Name = "--format", Aliases = ["-f"], Description = "Output format: json (default when piped) or text.", Required = false)]
    public string? Format { get; set; }

    protected abstract ILogger Logger { get; }

    protected abstract Task<int> ExecuteAsync();

    /// <summary>Called by DotMake. Implement <see cref="ExecuteAsync"/> instead.</summary>
    public async Task<int> RunAsync()
    {
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

        try
        {
            return await ExecuteAsync().ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitInvalidInput;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Error}", ex.Message);
            return ExitError;
        }
    }
}
