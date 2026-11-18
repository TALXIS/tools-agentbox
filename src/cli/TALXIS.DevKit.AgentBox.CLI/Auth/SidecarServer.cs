using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Auth;

internal sealed record SidecarProcessInfo(int ProcessId, long StartTimeUtcTicks, string ExecutablePath, string InstanceId);

internal static class SidecarFiles
{
    private static readonly JsonSerializerOptions JsonOptions = AgentBoxOutputJsonOptions.Default;

    public static string DirectoryPath(string homeDirectory) => Path.Combine(homeDirectory, ".agentbox");
    public static string PidPath(string homeDirectory) => Path.Combine(DirectoryPath(homeDirectory), "sidecar.pid");
    public static string LockPath(string homeDirectory) => Path.Combine(DirectoryPath(homeDirectory), "sidecar.lock");
    public static string LogPath(string homeDirectory) => Path.Combine(DirectoryPath(homeDirectory), "sidecar.log");

    public static SidecarProcessInfo? Read(string homeDirectory)
    {
        var path = PidPath(homeDirectory);
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<SidecarProcessInfo>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The sidecar PID file is invalid.", ex);
        }
    }

    public static void Write(string homeDirectory, SidecarProcessInfo process)
    {
        var path = PidPath(homeDirectory);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        PrivateFiles.EnsureDirectory(DirectoryPath(homeDirectory));
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(process, JsonOptions);
            using (var stream = new FileStream(temp, PrivateFiles.FileOptions(FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static void DeleteIfOwned(string homeDirectory, string instanceId)
    {
        var current = Read(homeDirectory);
        if (current?.InstanceId == instanceId && current.ProcessId == Environment.ProcessId)
            File.Delete(PidPath(homeDirectory));
    }

    public static void DeleteIfMatching(string homeDirectory, int processId, string instanceId)
    {
        var current = Read(homeDirectory);
        if (current?.InstanceId == instanceId && current.ProcessId == processId)
            File.Delete(PidPath(homeDirectory));
    }
}

internal sealed class SidecarServer(IHostInfo host, ITokenProvider tokenProvider, ILogger logger)
{
    public async Task RunAsync(string? instanceId, CancellationToken cancellationToken)
    {
        var id = instanceId ?? Guid.NewGuid().ToString("N");
        var handler = new TokenRequestHandler(tokenProvider, logger);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls($"http://127.0.0.1:{AadAuth.Port}");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new AgentBoxFileLoggerProvider(SidecarFiles.LogPath(host.HomeDirectory)));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsAllowedHost(context.Request.Host))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new TokenErrorResponse("invalid_host"), cancellationToken).ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });

        app.MapMethods(AadAuth.EndpointPath, ["GET", "POST"], async (HttpContext context) =>
        {
            var query = context.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, string?> form = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType)
            {
                var parsed = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                form = parsed.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            }

            var request = new TokenRequest(
                context.Request.Headers["Metadata"].FirstOrDefault(),
                query,
                form,
                context.Request.Headers["Secret"].FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
            var result = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = result.StatusCode;
            await context.Response.WriteAsJsonAsync(result.Body, cancellationToken).ConfigureAwait(false);
        });
        app.MapGet("/_agentbox/ready", (HttpContext context) =>
        {
            if (!string.Equals(context.Request.Query["instanceId"], id, StringComparison.Ordinal))
                return Results.NotFound();
            return Results.Json(new { processId = Environment.ProcessId, instanceId = id });
        });

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        using (var process = Process.GetCurrentProcess())
        {
            SidecarFiles.Write(host.HomeDirectory, new SidecarProcessInfo(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                Environment.ProcessPath ?? "",
                id));
        }

        logger.LogInformation("AgentBox sidecar listening on 127.0.0.1:{Port}.", AadAuth.Port);
        try
        {
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SidecarFiles.DeleteIfOwned(host.HomeDirectory, id);
        }
    }

    private static bool IsAllowedHost(HostString host)
    {
        var hostname = host.Host;
        return (string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hostname, "127.0.0.1", StringComparison.Ordinal))
            && (host.Port is null || host.Port == AadAuth.Port);
    }
}
