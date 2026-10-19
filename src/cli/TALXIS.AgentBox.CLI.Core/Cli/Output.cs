// Copied from TALXIS/tools-cli @ d70a60e:
//   src/TALXIS.CLI.Core/Shared/OutputFormat.cs, OutputContext.cs, OutputWriter.cs,
//   OutputFormatter.cs, TxcOutputJsonOptions.cs
// Kept: the stdout contract (results only, JSON or text, TTY auto-detection).
// Removed: telemetry support context on failures, WriteDynamicTable, WriteRaw.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.AgentBox.CLI.Core.Cli;

public enum OutputFormat
{
    Text = 0,
    Json = 1,
}

/// <summary>
/// Ambient output format for the current invocation, set from <c>--format</c>. Without it:
/// JSON when stdout is redirected (pipes, scripts), text for interactive terminals.
/// </summary>
public static class OutputContext
{
    private static readonly AsyncLocal<OutputFormat?> _format = new();

    public static OutputFormat Format
    {
        get => _format.Value ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Text);
        set => _format.Value = value;
    }

    public static bool IsJson => Format == OutputFormat.Json;

    public static void Reset() => _format.Value = null;
}

/// <summary>
/// The only stdout sink. Command code uses <see cref="OutputFormatter"/>; text renderers may use
/// this to write their table rows.
/// </summary>
public static class OutputWriter
{
    private static readonly AsyncLocal<TextWriter?> _writer = new();

#pragma warning disable RS0030 // Approved stdout sink
    private static TextWriter CurrentWriter => _writer.Value ?? Console.Out;
#pragma warning restore RS0030

    /// <summary>Redirects output for the current async flow (used by tests).</summary>
    public static IDisposable RedirectTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var previous = _writer.Value;
        _writer.Value = writer;
        return new RedirectScope(previous);
    }

    public static void WriteLine(string message) => CurrentWriter.WriteLine(message);

    public static void WriteLine() => CurrentWriter.WriteLine();

    private sealed class RedirectScope(TextWriter? previous) : IDisposable
    {
        public void Dispose() => _writer.Value = previous;
    }
}

/// <summary>Writes command results to stdout in the active <see cref="OutputContext.Format"/>.</summary>
public static class OutputFormatter
{
    public static void WriteData<T>(T data, Action<T>? textRenderer = null)
    {
        if (OutputContext.IsJson || textRenderer == null)
            OutputWriter.WriteLine(JsonSerializer.Serialize(data, AgentBoxOutputJsonOptions.Default));
        else
            textRenderer(data);
    }

    public static void WriteList<T>(IReadOnlyList<T> items, Action<IReadOnlyList<T>>? tableRenderer = null)
    {
        if (OutputContext.IsJson || tableRenderer == null)
            OutputWriter.WriteLine(JsonSerializer.Serialize(items, AgentBoxOutputJsonOptions.Default));
        else
            tableRenderer(items);
    }

    /// <summary>Result envelope for commands that change something.</summary>
    public static void WriteResult(string status, string? message = null, string? id = null)
    {
        if (OutputContext.IsJson)
            OutputWriter.WriteLine(JsonSerializer.Serialize(new CommandResultEnvelope(status, message, id), AgentBoxOutputJsonOptions.Default));
        else
            OutputWriter.WriteLine(message ?? status);
    }

    private sealed record CommandResultEnvelope(string Status, string? Message, string? Id);
}

/// <summary>Single source of truth for JSON on stdout.</summary>
public static class AgentBoxOutputJsonOptions
{
    public static readonly JsonSerializerOptions Default = Build(indented: true);

    /// <summary>Single-line variant, for JSON log lines on stderr.</summary>
    public static readonly JsonSerializerOptions Compact = Build(indented: false);

    private static JsonSerializerOptions Build(bool indented)
    {
#pragma warning disable RS0030 // This IS the approved JsonSerializerOptions factory
        var options = new JsonSerializerOptions
#pragma warning restore RS0030
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return options;
    }
}
