namespace FlareQuotes.Core.Security;

public static class TrustedExternalLinkPolicy
{
    private static readonly string[] ApprovedDomainRoots =
        ["flarefireplaces.com", "flareorder.com"];

    public static bool TryNormalize(string? value, out string normalizedUrl)
    {
        return TryNormalizeForHost(value, IsApprovedHost, out normalizedUrl);
    }

    public static bool TryNormalizeConsultation(string? value, out string normalizedUrl)
    {
        return TryNormalize(value, out normalizedUrl) ||
               TryNormalizeForHost(
                   value,
                   host => string.Equals(host, "meetings.hubspot.com", StringComparison.OrdinalIgnoreCase),
                   out normalizedUrl);
    }

    private static bool TryNormalizeForHost(string? value, Func<string, bool> isApprovedHost,
                                            out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrWhiteSpace(uri.IdnHost) ||
            !isApprovedHost(uri.IdnHost))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    public static bool IsApprovedHost(string? host)
    {
        var normalizedHost = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        return ApprovedDomainRoots.Any(root =>
            normalizedHost.Equals(root, StringComparison.Ordinal) ||
            normalizedHost.EndsWith('.' + root, StringComparison.Ordinal));
    }
}
