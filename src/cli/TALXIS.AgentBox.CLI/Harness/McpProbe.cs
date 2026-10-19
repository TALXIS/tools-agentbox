using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TALXIS.AgentBox.CLI.Harness;

/// <summary>An MCP server as a plugin declares it in its mcp.json.</summary>
public sealed record McpServer(string Name, string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env)
{
    /// <summary>The stdio servers declared in a plugin directory's <c>mcp.json</c> (or <c>.mcp.json</c>).</summary>
    public static IReadOnlyList<McpServer> DeclaredIn(string pluginDirectory)
    {
        var file = new[] { "mcp.json", ".mcp.json" }.Select(f => Path.Combine(pluginDirectory, f)).FirstOrDefault(File.Exists);
        if (file is null || JsonNode.Parse(File.ReadAllText(file))?["mcpServers"] is not JsonObject servers)
            return [];
        return servers
            .Where(s => s.Value?["command"] is not null)
            .Select(s => new McpServer(
                s.Key,
                s.Value!["command"]!.GetValue<string>(),
                s.Value["args"]?.AsArray().Select(a => a!.GetValue<string>()).ToList() ?? [],
                s.Value["env"]?.AsObject().ToDictionary(e => e.Key, e => e.Value!.GetValue<string>()) ?? []))
            .ToList();
    }
}

public sealed record McpProbeResult(string Server, bool Answered, int Tools, string? Error = null)
{
    public bool Healthy => Answered && Tools > 0;
}

/// <summary>
/// Starts an MCP server and does a real handshake: <c>initialize</c>, then <c>tools/list</c>.
/// Healthy means it answered and offers at least one tool. No credentials needed.
/// </summary>
public static class McpProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90); // the first dnx run downloads the server

    public static async Task<McpProbeResult> ProbeAsync(McpServer server, string? resolvedCommand)
    {
        var startInfo = new ProcessStartInfo(resolvedCommand ?? server.Command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in server.Args)
            startInfo.ArgumentList.Add(arg);
        foreach (var (key, value) in server.Env)
            startInfo.Environment[key] = value;

        using var timeout = new CancellationTokenSource(Timeout);
        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new(server.Name, false, 0, ex.Message);
        }
        using (process)
        {
            var log = process.StandardError.ReadToEndAsync(timeout.Token); // drained so the server never blocks on it
            try
            {
                var result = await HandshakeAsync(server.Name, process.StandardInput, process.StandardOutput, timeout.Token).ConfigureAwait(false);
                if (result.Answered)
                    return result;
                // Why it failed is usually the last thing it logged.
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                var lastLine = await LastLineAsync(log).ConfigureAwait(false);
                return lastLine is null ? result : result with { Error = $"{result.Error}: {lastLine}" };
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string?> LastLineAsync(Task<string> log)
    {
        try
        {
            return (await log.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    internal static async Task<McpProbeResult> HandshakeAsync(string name, TextWriter input, TextReader output, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(input, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "agentbox", version = "1" } } }).ConfigureAwait(false);
            await ReadResponseAsync(output, 1, cancellationToken).ConfigureAwait(false);
            await SendAsync(input, new { jsonrpc = "2.0", method = "notifications/initialized" }).ConfigureAwait(false);
            await SendAsync(input, new { jsonrpc = "2.0", id = 2, method = "tools/list" }).ConfigureAwait(false);
            var tools = await ReadResponseAsync(output, 2, cancellationToken).ConfigureAwait(false);
            var count = tools.TryGetProperty("tools", out var list) ? list.GetArrayLength() : 0;
            return new(name, true, count, count == 0 ? "offers no tools" : null);
        }
        catch (OperationCanceledException)
        {
            return new(name, false, 0, $"no answer within {Timeout.TotalSeconds:0} s");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            return new(name, false, 0, ex.Message);
        }
    }

    private static async Task SendAsync(TextWriter input, object message)
    {
        await input.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Reads lines until the response to <paramref name="id"/>, skipping notifications.</summary>
    private static async Task<JsonElement> ReadResponseAsync(TextReader output, int id, CancellationToken cancellationToken)
    {
        while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.TrimStart().StartsWith('{'))
                continue;
            using var message = JsonDocument.Parse(line);
            if (!message.RootElement.TryGetProperty("id", out var messageId) || messageId.ValueKind != JsonValueKind.Number || messageId.GetInt32() != id)
                continue;
            if (message.RootElement.TryGetProperty("error", out var error))
                throw new InvalidDataException(error.TryGetProperty("message", out var text) ? text.GetString() : "error response");
            return message.RootElement.GetProperty("result").Clone();
        }
        throw new InvalidDataException("the server closed its output");
    }
}
