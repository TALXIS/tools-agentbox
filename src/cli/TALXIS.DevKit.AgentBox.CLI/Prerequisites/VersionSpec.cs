using NuGet.Versioning;

namespace TALXIS.DevKit.AgentBox.CLI.Prerequisites;

/// <summary>
/// A tool version as the manifest asks for it, with the convention tool version managers share
/// (mise, asdf, nvm, actions/setup-*, the devcontainer Features' own "version" option):
/// <c>latest</c> for any version, <c>22</c> or <c>22.3</c> for the newest release starting with
/// those components, <c>22.3.1</c> for exactly that one. Anything else a Feature accepts, such as
/// <c>lts</c> or <c>os-provided</c>, can't be checked against an installed version, so it allows any.
/// </summary>
public sealed record VersionSpec
{
    public static readonly VersionSpec Any = new(null, 0);

    private readonly NuGetVersion? _version;
    private readonly int _components;

    private VersionSpec(NuGetVersion? version, int components)
    {
        _version = version;
        _components = components;
    }

    public bool IsAny => _version is null;

    /// <summary>Every component given (<c>22.3.1</c>): only that version satisfies it.</summary>
    public bool IsExact => _version is not null && _components >= 3;

    public static VersionSpec Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || !char.IsAsciiDigit(spec.Trim()[0]))
            return Any;
        // 10.0.x and 10.0.* (actions/setup-dotnet, NuGet floating versions) mean what 10.0 means.
        var text = spec.Trim();
        while (text.EndsWith(".x", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".*", StringComparison.Ordinal))
            text = text[..^2];
        var components = text.Split('-', '+')[0].Split('.').Length;
        // NuGet reads "22" as 22.0.0; the component count keeps what was actually asked for.
        return NuGetVersion.TryParse(text, out var version) ? new(version, components) : Any;
    }

    public bool IsSatisfiedBy(NuGetVersion installed)
    {
        if (_version is null)
            return true;
        if (IsExact)
            return VersionComparer.Version.Equals(installed, _version);
        // A partial version is a floating one: the stable releases it starts.
        return !installed.IsPrerelease
            && installed.Major == _version.Major
            && (_components < 2 || installed.Minor == _version.Minor);
    }

    public override string ToString() =>
        _version is null ? "any"
        : IsExact ? _version.ToNormalizedString()
        : _components == 1 ? $"{_version.Major}"
        : $"{_version.Major}.{_version.Minor}";
}
