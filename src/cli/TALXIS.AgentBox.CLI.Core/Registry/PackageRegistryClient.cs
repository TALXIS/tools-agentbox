using System.Net.Http.Json;
using System.Text.Json;
using NuGet.Versioning;

namespace TALXIS.AgentBox.CLI.Core.Registry;

/// <summary>Latest stable versions from nuget.org and the npm registry.</summary>
public interface IPackageRegistryClient
{
    Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken);
    Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken);
}

public sealed class PackageRegistryClient(HttpClient http) : IPackageRegistryClient
{
    // One shared instance (BannedSymbols forbids per-call HttpClient construction).
    public static readonly PackageRegistryClient Shared = new(CreateHttpClient());

    public async Task<NuGetVersion?> GetLatestNuGetVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
        using var document = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => NuGetVersion.TryParse(v.GetString(), out var parsed) ? parsed : null)
            .Where(v => v is not null && !v.IsPrerelease)
            .Max();
    }

    public async Task<NuGetVersion?> GetLatestNpmVersionAsync(string packageName, CancellationToken cancellationToken)
    {
        var url = $"https://registry.npmjs.org/{packageName.Replace("/", "%2F", StringComparison.Ordinal)}/latest";
        var latest = await http.GetFromJsonAsync<JsonElement>(url, cancellationToken).ConfigureAwait(false);
        return NuGetVersion.TryParse(latest.GetProperty("version").GetString(), out var version) ? version : null;
    }

    private static HttpClient CreateHttpClient()
    {
#pragma warning disable RS0030 // The single shared instance
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
#pragma warning restore RS0030
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TALXIS.AgentBox.CLI");
        return client;
    }
}
