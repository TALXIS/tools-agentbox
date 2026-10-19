using System.Text;
using NuGet.Versioning;
using TALXIS.AgentBox.CLI.Core;
using TALXIS.AgentBox.CLI.Core.Cli;
using TALXIS.AgentBox.CLI.Core.Features;
using TALXIS.AgentBox.CLI.Core.Host;
using TALXIS.AgentBox.CLI.Core.Registry;

// Command tests swap the static AgentBoxServices.Current, so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TALXIS.AgentBox.CLI.Tests;

internal sealed class FakeHost : IHostInfo
{
    public OsKind Os { get; init; } = OsKind.Linux;
    public HostClass HostClass { get; init; } = HostClass.Container;
    public bool IsRoot { get; init; } = true;
    public string HomeDirectory { get; init; } = Directory.CreateTempSubdirectory("agentbox-home-").FullName;
    public Dictionary<string, string> Environment { get; } = new();
    public HashSet<string> Files { get; } = new();

    public string? GetEnvironmentVariable(string name) => Environment.GetValueOrDefault(name);
    public bool FileExists(string path) => Files.Contains(path) || File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

internal sealed class FakeProcesses : IProcessRunner
{
    public Dictionary<string, string> OnPath { get; } = new();
    public List<(string File, string[] Args)> Calls { get; } = new();
    public Func<string, IReadOnlyList<string>, ProcessResult> Handler { get; set; } = (_, _) => new ProcessResult(0, "", "", false);

    public string? FindOnPath(string command) => OnPath.GetValueOrDefault(command);

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments.ToArray()));
        return Task.FromResult(Handler(fileName, arguments));
    }
}

internal sealed class FakeRegistry : IPackageRegistryClient
{
    public Dictionary<string, string> Latest { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Offline { get; set; }

    public Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken) => Get(packageId);
    public Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken) => Get(packageName);

    private Task<NuGetVersion?> Get(string id) => Offline
        ? Task.FromException<NuGetVersion?>(new HttpRequestException("offline"))
        : Task.FromResult(Latest.TryGetValue(id, out var v) ? NuGetVersion.Parse(v) : null);
}

/// <summary>Installs fakes as <see cref="AgentBoxServices.Current"/> and captures stdout for one test.</summary>
internal sealed class CommandHarness : IDisposable
{
    private readonly AgentBoxServices _previous = AgentBoxServices.Current;
    private readonly IDisposable _redirect;
    private readonly StringBuilder _output = new();

    public CommandHarness(FakeHost host, FakeProcesses? processes = null, FakeRegistry? registry = null)
    {
        Host = host;
        Processes = processes ?? new FakeProcesses();
        Registry = registry ?? new FakeRegistry();
        AgentBoxServices.Current = new AgentBoxServices(Host, Processes, Registry, () => null);
        _redirect = OutputWriter.RedirectTo(new StringWriter(_output));
    }

    public FakeHost Host { get; }
    public FakeProcesses Processes { get; }
    public FakeRegistry Registry { get; }
    public string Output => _output.ToString();

    public void Dispose()
    {
        _redirect.Dispose();
        AgentBoxServices.Current = _previous;
    }
}

internal static class Repo
{
    /// <summary>A file in this repository, found by walking up from the test binaries.</summary>
    public static string File(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (System.IO.File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
