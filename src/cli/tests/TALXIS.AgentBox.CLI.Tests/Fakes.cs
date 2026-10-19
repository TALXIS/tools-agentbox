using System.Text;
using NuGet.Versioning;
using TALXIS.AgentBox.CLI.Cli;
using TALXIS.AgentBox.CLI.Host;
using TALXIS.AgentBox.CLI.Prerequisites;
using TALXIS.AgentBox.CLI.Registry;

// Command tests swap the static AgentBoxServices.Current, so they can't run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TALXIS.AgentBox.CLI.Tests;

internal sealed class FakeHost : IHostInfo
{
    public OsKind Os { get; init; } = OsKind.Linux;
    public bool IsContainer { get; init; } = true;
    public bool IsRoot { get; init; }
    public bool IsInteractive { get; init; }
    public string HomeDirectory { get; } = Directory.CreateTempSubdirectory("agentbox-home-").FullName;
    public Dictionary<string, string> Environment { get; } = new();
    public HashSet<string> Files { get; } = new();

    public string? GetEnvironmentVariable(string name) => Environment.GetValueOrDefault(name);
    public bool FileExists(string path) => Files.Contains(path) || File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

internal sealed class FakeProcesses : IProcessRunner
{
    public Dictionary<string, string> OnPath { get; } = new();
    public List<string> Calls { get; } = new();
    public Func<string, IReadOnlyList<string>, ProcessResult> Handler { get; set; } = (_, _) => new(0, "", "", false);

    public string? FindOnPath(string command) => OnPath.GetValueOrDefault(command);

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null, string? workingDirectory = null, CancellationToken cancellationToken = default,
        bool showErrors = false)
    {
        Calls.Add($"{fileName} {string.Join(' ', arguments)}");
        return Task.FromResult(Handler(fileName, arguments));
    }

    /// <summary>Every catalog command on PATH, with a .NET 10 SDK and Node 22.</summary>
    public static FakeProcesses WithEverything()
    {
        var processes = new FakeProcesses
        {
            Handler = (file, args) => (file, string.Join(' ', args)) switch
            {
                ("dotnet", "--list-sdks") => new(0, "10.0.100 [/usr/share/dotnet/sdk]\n", "", false),
                ("node", "--version") => new(0, "v22.11.0\n", "", false),
                _ => new(0, "", "", false),
            },
        };
        foreach (var p in PrerequisiteCatalog.All)
            processes.OnPath[p.Command] = "/usr/bin/" + p.Command;
        return processes;
    }
}

internal sealed class FakeRegistry : IPackageRegistryClient
{
    public Dictionary<string, string> Latest { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Offline { get; init; }

    public Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken) => Get(packageId);
    public Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken) => Get(packageName);

    private Task<NuGetVersion?> Get(string id) => Offline
        ? Task.FromException<NuGetVersion?>(new HttpRequestException("offline"))
        : Task.FromResult(Latest.TryGetValue(id, out var v) ? NuGetVersion.Parse(v) : null);
}

internal sealed class FakePrompt(string? answer = null) : IPrompt
{
    public int Asked { get; private set; }

    public string? Ask(string question)
    {
        Asked++;
        return answer;
    }
}

/// <summary>Installs fakes as <see cref="AgentBoxServices.Current"/> and captures stdout for one test.</summary>
internal sealed class CommandHarness : IDisposable
{
    private readonly AgentBoxServices _previous = AgentBoxServices.Current;
    private readonly IDisposable _redirect;
    private readonly StringBuilder _output = new();

    public CommandHarness(FakeHost host, FakeProcesses? processes = null, FakeRegistry? registry = null, IPrompt? prompt = null)
    {
        AgentBoxServices.Current = new AgentBoxServices(host, processes ?? new FakeProcesses(), registry ?? new FakeRegistry(), prompt ?? new FakePrompt(), null, null);
        _redirect = OutputWriter.RedirectTo(new StringWriter(_output));
    }

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
