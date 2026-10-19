using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TALXIS.AgentBox.CLI.Cli;

namespace TALXIS.AgentBox.CLI.Features;

/// <summary>One resolved Feature: <c>registry/path@sha256:digest</c> plus its options.</summary>
public sealed record LockedFeature(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("options")] Dictionary<string, JsonElement>? Options)
{
    public string Registry => Id[..Id.IndexOf('/', StringComparison.Ordinal)];

    /// <summary>Repository path without registry or digest, e.g. <c>devcontainers/features/node</c>.</summary>
    public string Path
    {
        get
        {
            var rest = Id[(Id.IndexOf('/', StringComparison.Ordinal) + 1)..];
            var at = rest.IndexOf('@', StringComparison.Ordinal);
            return at < 0 ? rest : rest[..at];
        }
    }

    public string Digest => Id[(Id.LastIndexOf('@') + 1)..];
}

/// <summary>
/// The AgentBox Feature list, resolved to digests and install order when the package is built
/// (build/pack.sh) and embedded in it, so every release installs the same Feature versions.
/// </summary>
public sealed record FeatureLock(
    [property: JsonPropertyName("features")] IReadOnlyList<LockedFeature> Features)
{
    public const string ResourceName = "features.lock.json";

    public static FeatureLock? LoadEmbedded()
    {
        using var stream = typeof(FeatureLock).Assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? null : Parse(stream);
    }

    public static FeatureLock? Parse(Stream stream) =>
        JsonSerializer.Deserialize<FeatureLock>(stream, AgentBoxOutputJsonOptions.Default);
}
