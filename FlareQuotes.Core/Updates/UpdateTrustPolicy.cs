using System.Text.RegularExpressions;

namespace FlareQuotes.Core.Updates;

/// <summary>
/// Fixed trust policy for the Flare-managed GitHub update lane.
/// This does not replace publisher code signing, but it prevents manifests from
/// redirecting the app to an arbitrary host or unexpected release asset.
/// </summary>
public static partial class UpdateTrustPolicy
{
    public const int MaxManifestBytes = 64 * 1024;
    public const long MaxInstallerBytes = 300L * 1024 * 1024;
    public const string ManifestUrl =
        "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json";

    private const string ReleasePathPrefix =
        "/kbAppDev/flare-fireplace-quotes-updates/releases/download/v";
    private const string InstallerAssetName = "Flare.Fireplace.Quotes.exe";

    public static bool TryGetTrustedManifestUri(string? candidate, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            return false;

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment) ||
            !string.Equals(parsed.AbsoluteUri, ManifestUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool TryGetTrustedInstallerUri(string? candidate, string? version, out Uri uri)
    {
        uri = null!;
        if (!IsValidVersion(version) || !Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            return false;

        var expectedPath = $"{ReleasePathPrefix}{version}/{InstallerAssetName}";

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment) ||
            !string.Equals(parsed.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsTrustedDownloadResponseUri(Uri? uri)
    {
        if (uri is null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "github-releases.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && Version.TryParse(version, out var parsed) && parsed.Major >= 1 &&
        VersionRegex().IsMatch(version);

    public static bool IsValidSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Sha256Regex().IsMatch(value.Trim());

    public static bool IsValidInstallerSize(long sizeBytes) =>
        sizeBytes > 0 && sizeBytes <= MaxInstallerBytes;

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
