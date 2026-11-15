using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Host;
using TALXIS.DevKit.AgentBox.CLI.Registry;

namespace TALXIS.DevKit.AgentBox.CLI.Auth;

internal sealed record SidecarStatus(bool Running, int? Pid, int Port, bool Authenticated, string? Tenant, string? User);

internal sealed class SidecarLifecycle(IHostInfo host, AuthService auth, ILogger logger)
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    public async Task<(bool Started, int? Pid)> StartAsync(CancellationToken cancellationToken)
    {
        using var lifecycleLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var existing = SidecarFiles.Read(host.HomeDirectory);
        if (existing is not null)
        {
            using var running = GetOwnedProcess(existing);
            if (running is not null && await IsReadyAsync(existing, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
                return (false, existing.ProcessId);
            if (running is not null)
                throw new InvalidOperationException("The recorded AgentBox process is running but its sidecar endpoint is not ready; refusing to start another instance.");
            File.Delete(SidecarFiles.PidPath(host.HomeDirectory));
        }

        var instanceId = Guid.NewGuid().ToString("N");
        var (executable, arguments) = ChildCommand(instanceId);
        using var logFile = new FileStream(
            SidecarFiles.LogPath(host.HomeDirectory),
            PrivateFiles.FileOptions(FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["AGENTBOX_LOG_FILE"] = SidecarFiles.LogPath(host.HomeDirectory);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Could not start the AgentBox sidecar process.");
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();

        try
        {
            var deadline = DateTimeOffset.UtcNow + StartupTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException($"AgentBox sidecar exited during startup. {ReadLogTail()}");

                var info = SidecarFiles.Read(host.HomeDirectory);
                if (info?.InstanceId == instanceId && info.ProcessId == process.Id
                    && await IsReadyAsync(info, TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false))
                    return (true, process.Id);

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"AgentBox sidecar did not become ready within {StartupTimeout.TotalSeconds:0} seconds. {ReadLogTail()}");
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            SidecarFiles.DeleteIfMatching(host.HomeDirectory, process.Id, instanceId);
            throw;
        }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        using var lifecycleLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var info = SidecarFiles.Read(host.HomeDirectory);
        if (info is null)
            return false;

        using var process = GetOwnedProcess(info);
        if (process is null)
        {
            File.Delete(SidecarFiles.PidPath(host.HomeDirectory));
            return false;
        }

        if (!await IsReadyAsync(info, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("The recorded sidecar process is not serving its readiness endpoint; leaving that process untouched.");
            File.Delete(SidecarFiles.PidPath(host.HomeDirectory));
            return false;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        SidecarFiles.DeleteIfMatching(host.HomeDirectory, info.ProcessId, info.InstanceId);
        return true;
    }

    public async Task<SidecarStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var info = SidecarFiles.Read(host.HomeDirectory);
        using var process = info is null ? null : GetOwnedProcess(info);
        var running = info is not null && process is not null
            && await IsReadyAsync(info, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

        if (!running)
        {
            if (info is not null && process is null)
                File.Delete(SidecarFiles.PidPath(host.HomeDirectory));
            return new SidecarStatus(false, null, AadAuth.Port, false, null, null);
        }

        var account = await auth.GetActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        return new SidecarStatus(true, info!.ProcessId, AadAuth.Port, account is not null, account?.TenantId, account?.Username);
    }

    private static (string Executable, string[] Arguments) ChildCommand(string instanceId)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the current executable path.");
        var arguments = new List<string>();
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assembly))
                throw new InvalidOperationException("Cannot restart AgentBox when hosted by dotnet because the entry assembly path is unavailable.");
            arguments.Add(assembly);
        }
        arguments.AddRange(["auth", "sidecar", "run", "--instance-id", instanceId]);
        return (executable, [.. arguments]);
    }

    private async Task<bool> IsReadyAsync(SidecarProcessInfo info, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var response = await Http.Shared.GetAsync(
                $"http://127.0.0.1:{AadAuth.Port}/_agentbox/ready?instanceId={Uri.EscapeDataString(info.InstanceId)}",
                timeoutSource.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false), cancellationToken: timeoutSource.Token).ConfigureAwait(false);
            var root = document.RootElement;
            return root.GetProperty("processId").GetInt32() == info.ProcessId
                && root.GetProperty("instanceId").GetString() == info.InstanceId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private Process? GetOwnedProcess(SidecarProcessInfo info)
    {
        try
        {
            var process = Process.GetProcessById(info.ProcessId);
            var startTicks = process.StartTime.ToUniversalTime().Ticks;
            // On Linux, StartTime is derived from /proc jiffies plus a boot time recomputed on every read,
            // so separate processes observe slightly different values.
            var sameStart = Math.Abs(startTicks - info.StartTimeUtcTicks) <= StartTimeTolerance.Ticks;
            var processPath = process.MainModule?.FileName;
            var sameExecutable = !string.IsNullOrWhiteSpace(info.ExecutablePath)
                && !string.IsNullOrWhiteSpace(processPath)
                && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(info.ExecutablePath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (sameStart && sameExecutable)
                return process;
            process.Dispose();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        return null;
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        PrivateFiles.EnsureDirectory(SidecarFiles.DirectoryPath(host.HomeDirectory));
        var deadline = DateTimeOffset.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    SidecarFiles.LockPath(host.HomeDirectory),
                    PrivateFiles.FileOptions(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for the AgentBox sidecar lifecycle lock.");
        }
    }

    private string ReadLogTail()
    {
        var path = SidecarFiles.LogPath(host.HomeDirectory);
        if (!File.Exists(path))
            return $"Log: {path}";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == 20)
                    lines.Dequeue();
                lines.Enqueue(line);
            }
            return $"Log: {path}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
        }
        catch (IOException ex)
        {
            return $"Log: {path}{Environment.NewLine}Could not read log tail: {ex.Message}";
        }
    }
}
