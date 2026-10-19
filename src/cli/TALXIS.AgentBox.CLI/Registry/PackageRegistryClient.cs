using System.Net.Http.Json;
using System.Text.Json;
using NuGet.Versioning;

namespace TALXIS.AgentBox.CLI.Registry;

/// <summary>Latest stable versions from nuget.org and the npm registry.</summary>
public interface IPackageRegistryClient
{
    Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken);
    Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken);
}

public sealed class PackageRegistryClient(HttpClient http) : IPackageRegistryClient
{
    /// <summary>The latest listed stable version. nuget.org's search knows what a publisher unlisted;
    /// its package index doesn't.</summary>
    public async Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        var index = await http.GetFromJsonAsync<JsonElement>("https://api.nuget.org/v3/index.json", cancellationToken).ConfigureAwait(false);
        var search = index.GetProperty("resources").EnumerateArray()
            .First(r => r.GetProperty("@type").GetString() == "SearchQueryService").GetProperty("@id").GetString();
        var result = await http.GetFromJsonAsync<JsonElement>(
            $"{search}?q=packageid:{Uri.EscapeDataString(packageId)}&prerelease=false&semVerLevel=2.0.0&take=1", cancellationToken).ConfigureAwait(false);
        return result.GetProperty("data").EnumerateArray()
            .Select(p => NuGetVersion.TryParse(p.GetProperty("version").GetString(), out var version) ? version : null)
            .FirstOrDefault();
    }

    public async Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken)
    {
        var url = $"https://registry.npmjs.org/{packageName.Replace("/", "%2F", StringComparison.Ordinal)}/latest";
        var latest = await http.GetFromJsonAsync<JsonElement>(url, cancellationToken).ConfigureAwait(false);
        return NuGetVersion.TryParse(latest.GetProperty("version").GetString(), out var version) ? version : null;
    }
}

/// <summary>The one HttpClient (BannedSymbols forbids creating them per call).</summary>
public static class Http
{
#pragma warning disable RS0030 // The single shared instance
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(5), DefaultRequestHeaders = { { "User-Agent", "TALXIS.AgentBox.CLI" } } };
#pragma warning restore RS0030
}
