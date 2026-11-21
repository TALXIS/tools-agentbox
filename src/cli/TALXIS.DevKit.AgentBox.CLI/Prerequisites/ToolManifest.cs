using System.Text.Json;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

/// <summary>
/// src/container/agentbox-features.json, embedded in every build: the one place that says which
/// tools AgentBox installs and which version of each (each Feature's "version" option). The catalog
/// only knows how to find and check a tool; the version it must have comes from here.
/// </summary>
public sealed class ToolManifest(IReadOnlyDictionary<string, VersionSpec> specs)
{
    public const string ResourceName = "agentbox-features.json";

    public static ToolManifest Empty { get; } = new(new Dictionary<string, VersionSpec>());

    public static ToolManifest Embedded { get; } = LoadEmbedded();

    private static ToolManifest LoadEmbedded()
    {
        using var stream = typeof(ToolManifest).Assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? Empty : Parse(stream);
    }

    public static ToolManifest Parse(Stream stream)
    {
        using var json = JsonDocument.Parse(stream);
        var specs = new Dictionary<string, VersionSpec>(StringComparer.Ordinal);
        foreach (var feature in json.RootElement.GetProperty("features").EnumerateObject())
        {
            var version = feature.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            specs[FeaturePath(feature.Name)] = VersionSpec.Parse(version);
        }
        return new(specs);
    }

    /// <summary>The version a prerequisite's Feature is installed at; any, for one without a Feature.</summary>
    public VersionSpec SpecFor(Prerequisite p) =>
        p.Feature is { } feature && specs.TryGetValue(feature, out var spec) ? spec : VersionSpec.Any;

    public IEnumerable<string> Features => specs.Keys;

    /// <summary><c>ghcr.io/devcontainers/features/node:1</c> → <c>devcontainers/features/node</c>.</summary>
    internal static string FeaturePath(string id)
    {
        var path = id[(id.IndexOf('/', StringComparison.Ordinal) + 1)..];
        var tag = path.LastIndexOf(':');
        return tag < 0 ? path : path[..tag];
    }
}
