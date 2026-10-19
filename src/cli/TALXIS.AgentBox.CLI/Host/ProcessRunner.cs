using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TALXIS.AgentBox.CLI.Host;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>One line saying why it failed, for reports: the last line naming an error, else the last line.</summary>
    public string FailureSummary =>
        TimedOut ? "timed out"
        : ErrorLine(StandardError) ?? ErrorLine(StandardOutput) ?? Lines(StandardError).LastOrDefault() ?? Lines(StandardOutput).LastOrDefault()
          ?? $"exit code {ExitCode}";

    private static string? ErrorLine(string text) => Lines(text).LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public interface IProcessRunner
{
    /// <summary>Full path of <paramref name="command"/> on PATH (PATHEXT-aware on Windows), or null.</summary>
    string? FindOnPath(string command);

    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    // A background process the command started can keep its output open long after it exits.
    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(5);

    public string? FindOnPath(string command)
    {
        if (Path.IsPathRooted(command))
            return File.Exists(command) ? command : null;

        // On Windows only PATHEXT files can be started: npm, for example, ships an extensionless
        // shell script next to npm.cmd.
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Path.HasExtension(command)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate) && (OperatingSystem.IsWindows() || IsExecutable(candidate)))
                    return candidate;
            }
        }
        return null;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(FindOnPath(fileName) ?? fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                    startInfo.Environment.Remove(key);
                else
                    startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ProcessResult(127, "", ex.Message, TimedOut: false);
        }
        process.StandardInput.Close();

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var reading = Task.WhenAll(PumpAsync(process.StandardOutput, stdout), PumpAsync(process.StandardError, stderr));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            if (cancellationToken.IsCancellationRequested)
                throw;
            timedOut = true;
        }

        await Task.WhenAny(reading, Task.Delay(OutputGrace, CancellationToken.None)).ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : process.ExitCode, Snapshot(stdout), Snapshot(stderr), timedOut);
    }

    /// <summary>Copies a stream into <paramref name="into"/> as it arrives, so whatever came is there
    /// even if the stream never ends.</summary>
    private static async Task PumpAsync(StreamReader reader, StringBuilder into)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (into)
                    into.Append(buffer, 0, read);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process is gone and its streams with it.
        }
    }

    private static string Snapshot(StringBuilder text)
    {
        lock (text)
            return text.ToString();
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
}
