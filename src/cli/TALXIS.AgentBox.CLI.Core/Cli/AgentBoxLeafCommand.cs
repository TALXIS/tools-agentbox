// Copied from TALXIS/tools-cli @ d70a60e: src/TALXIS.CLI.Core/Shared/TxcLeafCommand.cs
// Kept: --format/-f, the exit-code contract, exception -> exit code mapping, the destructive gate.
// Removed: telemetry activity scope, profile identity tagging, the production guard, support
// escalation info, and the interactive confirmation prompt (destructive commands simply require
// --yes; agentbox v1 has none).
using System.Reflection;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;

namespace TALXIS.AgentBox.CLI.Core.Cli;

/// <summary>
/// Base class for every leaf (non-routing) command. Implement <see cref="ExecuteAsync"/>.
/// Exit codes: 0 success, 1 runtime error, 2 input/validation error,
/// 3 unhealthy machine (a prerequisite is missing or broken; an agentbox addition to txc's contract).
/// </summary>
public abstract class AgentBoxLeafCommand
{
    protected const int ExitSuccess = 0;
    protected const int ExitError = 1;
    protected const int ExitValidationError = 2;
    protected const int ExitUnhealthy = 3;

    [CliOption(
        Name = "--format",
        Aliases = ["-f"],
        Description = "Output format: json (default when piped) or text (default in terminal).",
        Required = false)]
    public string? Format { get; set; }

    protected abstract ILogger Logger { get; }

    /// <summary>Entry point called by DotMake. Do not hide it; implement <see cref="ExecuteAsync"/>.</summary>
    public async Task<int> RunAsync()
    {
        try
        {
            var formatError = ApplyOutputFormat();
            if (formatError.HasValue)
                return formatError.Value;

            if (GetType().GetCustomAttribute<CliDestructiveAttribute>() is { } destructive
                && (this as IDestructiveCommand)?.Yes != true)
            {
                Logger.LogError("{Impact} Re-run with --yes to confirm.", destructive.Impact);
                return ExitValidationError;
            }

            return await ExecuteAsync().ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // Bad input: the message is the whole story, no stack trace.
            Logger.LogError("{Error}", ex.Message);
            return ExitValidationError;
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning(ex, "Operation was cancelled.");
            return ExitError;
        }
        catch (Exception ex)
        {
            OutputFormatter.WriteResult("failed", ex.Message);
            Logger.LogError(ex, "Command failed: {Error}", ex.Message);
            return ExitError;
        }
    }

    protected abstract Task<int> ExecuteAsync();

    private int? ApplyOutputFormat()
    {
        OutputContext.Reset();
        if (Format is null)
            return null;
        if (Format.Equals("json", StringComparison.OrdinalIgnoreCase))
            OutputContext.Format = OutputFormat.Json;
        else if (Format.Equals("text", StringComparison.OrdinalIgnoreCase))
            OutputContext.Format = OutputFormat.Text;
        else
        {
            Logger.LogError("Unknown output format '{Format}'. Valid values: json, text.", Format);
            return ExitValidationError;
        }
        return null;
    }
}
