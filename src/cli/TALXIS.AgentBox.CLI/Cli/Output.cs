// Adapted from TALXIS/tools-cli so both CLIs share the output contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.AgentBox.CLI.Cli;

public enum OutputFormat { Text, Json }

/// <summary>Output format of this invocation: <c>--format</c>, else JSON when stdout is redirected.</summary>
public static class OutputContext
{
    private static readonly AsyncLocal<OutputFormat?> _format = new();

    public static OutputFormat Format
    {
        get => _format.Value ?? (Console.IsOutputRedirected ? OutputFormat.Json : OutputFormat.Text);
        set => _format.Value = value;
    }

    public static void Reset() => _format.Value = null;
}

/// <summary>The only stdout sink.</summary>
public static class OutputWriter
{
    private static readonly AsyncLocal<TextWriter?> _writer = new();

#pragma warning disable RS0030 // The approved stdout sink
    private static TextWriter Current => _writer.Value ?? Console.Out;
#pragma warning restore RS0030

    public static void WriteLine(string line) => Current.WriteLine(line);

    /// <summary>Redirects output for the current async flow (tests).</summary>
    public static IDisposable RedirectTo(TextWriter writer)
    {
        var previous = _writer.Value;
        _writer.Value = writer;
        return new Restore(() => _writer.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>Writes a command's result: JSON, or the command's own text rendering.</summary>
public static class OutputFormatter
{
    public static void Write<T>(T result, Action<T> renderText)
    {
        if (OutputContext.Format == OutputFormat.Json)
            OutputWriter.WriteLine(JsonSerializer.Serialize(result, AgentBoxOutputJsonOptions.Default));
        else
            renderText(result);
    }

    /// <summary>An enum value as it appears in JSON output, so text and JSON use the same words.</summary>
    public static string Label(Enum value) => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString());
}

public static class AgentBoxOutputJsonOptions
{
    public static readonly JsonSerializerOptions Default = Build(indented: true);

    public static readonly JsonSerializerOptions Compact = Build(indented: false);

    private static JsonSerializerOptions Build(bool indented)
    {
#pragma warning disable RS0030 // The one place options are built
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
