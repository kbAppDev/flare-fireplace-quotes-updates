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
        "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v2-latest.json";

    // This public key is intentionally compiled into the app. The corresponding private key
    // exists only as a tag-restricted GitHub Actions release-environment secret and signs each release manifest.
    // Keeping trust configuration out of user-editable settings prevents a local settings
    // change from replacing the update authority.
    public const string ManifestSigningPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAzZcQqRo5AffcBdpGcqXe
        QDKpSnvkpBOeaIbWWIgGowKhSIEP/yP21qFARzncxSKv8DJWgEsn9MstAyiUID0p
        KseYoluEpCpmxK+JopU7PlDxk3n9ZF3a831NBGX3VW7H3MdGDeNEB1gfqfHsRCwi
        M3QgYq6Eok2xf60L/zR1J4pVzTU+cDaeOaTuyneM3QHy+DRYAL8UtTGivSGiKCy8
        G0J7ISLDKkj3R9PJVcT4jdYBQ9XwQZ1gfXPw1dGnX2CCga68TmxPDyRy9X4/6KO2
        /3GpDGa0NWo908LiIqFrF2w26Mzinv9JlYEkK6lHQAnR5rid8ajICbtF6w1J902h
        hHWQDuVMympNPEKM5wfoB2GWdIrlGB3FqtaioVGyVFEdSsJocLNmqGTcKZUlyQgz
        VzusJdQD213qbnsNjKlbMPu/yOnZtk3eEREEpgi118iJZEVccZD0qcP0GBze/9aH
        mwcXBS7Q/rlvQEj0e5aTXipkPV6lkIKhSO8R3JLdVjJJAgMBAAE=
        -----END PUBLIC KEY-----
        """;

    public const string ManifestSigningPublicKeySha256 =
        "098c6b505a5cecbe6c060c80633f4283cc12597291113eb266ef3293bfff2066";

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
            !parsed.IsDefaultPort || !string.IsNullOrEmpty(parsed.UserInfo) ||
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
            !parsed.IsDefaultPort || !string.IsNullOrEmpty(parsed.UserInfo) ||
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
        if (uri is null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
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
