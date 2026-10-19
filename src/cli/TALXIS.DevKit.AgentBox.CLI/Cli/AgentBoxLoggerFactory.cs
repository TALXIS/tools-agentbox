// Adapted from TALXIS/tools-cli. All logs go to stderr: text for people, JSON lines when stdout is
// redirected or AGENTBOX_LOG_FORMAT=json. AGENTBOX_LOG_LEVEL sets the level.
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TALXIS.DevKit.AgentBox.CLI.Cli;

public static class AgentBoxLoggerFactory
{
    private static readonly Lazy<ILoggerFactory> _factory = new(() => LoggerFactory.Create(Configure));

    public static ILogger CreateLogger(string categoryName) => _factory.Value.CreateLogger(categoryName);

    private static void Configure(ILoggingBuilder builder)
    {
        var logFormat = Environment.GetEnvironmentVariable("AGENTBOX_LOG_FORMAT");
        var level = Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("AGENTBOX_LOG_LEVEL"), ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;
#pragma warning disable RS0030 // Log routing only
        var json = logFormat == "json" || (logFormat != "text" && Console.IsOutputRedirected);
#pragma warning restore RS0030

        builder.ClearProviders();
        builder.SetMinimumLevel(level);
        if (json)
        {
            builder.AddProvider(new JsonStderrLoggerProvider());
            return;
        }

        builder.AddConsole(options =>
        {
            options.FormatterName = AgentBoxConsoleFormatter.FormatterName;
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.AddConsoleFormatter<AgentBoxConsoleFormatter, AgentBoxConsoleFormatterOptions>(o => o.Verbose = level <= LogLevel.Debug);
    }
}

internal sealed class AgentBoxConsoleFormatter(IOptions<AgentBoxConsoleFormatterOptions> options) : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "agentbox";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            return;

        var (color, label) = logEntry.LogLevel switch
        {
            LogLevel.Trace => ("\x1b[90m", "[TRACE]"),
            LogLevel.Debug => ("\x1b[90m", "[DEBUG]"),
            LogLevel.Information => ("\x1b[36m", "[INFO] "),
            LogLevel.Warning => ("\x1b[33m", "[WARN] "),
            LogLevel.Error => ("\x1b[31m", "[ERROR]"),
            LogLevel.Critical => ("\x1b[91m", "[CRIT] "),
            _ => ("", "[?]    "),
        };
        textWriter.Write(color);
        textWriter.Write(label);
        textWriter.Write("\x1b[0m ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is null)
            return;
        if (options.Value.Verbose)
        {
            foreach (var line in logEntry.Exception.ToString().Split('\n'))
                textWriter.WriteLine("        " + line.TrimEnd('\r'));
            return;
        }
        for (var inner = logEntry.Exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(inner.Message) && !message.Contains(inner.Message, StringComparison.Ordinal))
                textWriter.WriteLine("        " + inner.Message);
        }
    }
}

internal sealed class AgentBoxConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool Verbose { get; set; }
}

internal sealed class JsonStderrLoggerProvider : ILoggerProvider
{
#pragma warning disable RS0030 // The approved stderr sink
    private readonly TextWriter _stderr = Console.Error;
#pragma warning restore RS0030

    public ILogger CreateLogger(string categoryName) => new JsonStderrLogger(categoryName, _stderr);

    public void Dispose() { }
}

internal sealed class JsonStderrLogger(string category, TextWriter stderr) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        var message = formatter(state, exception);
        if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
            message = $"{message} {exception.Message}";

        Dictionary<string, object?>? data = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            foreach (var property in properties)
            {
                if (property.Key == "{OriginalFormat}")
                    continue;
                data ??= [];
                data[property.Key] = property.Value?.ToString();
            }
        }

        var line = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["level"] = logLevel.ToString(),
            ["category"] = category,
            ["message"] = message,
            ["data"] = data,
        };
        stderr.WriteLine(JsonSerializer.Serialize(line, AgentBoxOutputJsonOptions.Compact));
    }
}
