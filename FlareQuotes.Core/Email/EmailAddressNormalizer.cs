using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace FlareQuotes.Core.Email;

public static partial class EmailAddressNormalizer
{
    [GeneratedRegex(
        @"[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailCandidateRegex();

    public static string NormalizeSingleOrEmpty(string? value)
    {
        return TryNormalizeSingle(value, out var normalized) ? normalized : string.Empty;
    }

    public static string ExtractFirstOrEmpty(string? value)
    {
        var cleaned = CleanCopiedText(value);
        foreach (Match match in EmailCandidateRegex().Matches(cleaned))
        {
            if (TryCanonicalize(match.Value, out var normalized))
                return normalized;
        }

        return string.Empty;
    }

    public static bool TryNormalizeSingle(string? value, out string normalized)
    {
        normalized = string.Empty;
        var cleaned = CleanCopiedText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        var matches = EmailCandidateRegex().Matches(cleaned);
        if (matches.Count != 1)
            return false;

        return TryCanonicalize(matches[0].Value, out normalized);
    }

    public static bool TryNormalizeList(string? value, out IReadOnlyList<string> normalized)
    {
        var results = new List<string>();
        normalized = results;

        var cleaned = CleanCopiedText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return true;

        var parts = cleaned.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!TryNormalizeSingle(part, out var address))
                return false;

            if (!results.Contains(address, StringComparer.OrdinalIgnoreCase))
                results.Add(address);
        }

        return results.Count > 0;
    }

    private static string CleanCopiedText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            // Invisible formatting marks (for example zero-width spaces) must be removed,
            // but line breaks and tabs must remain separators. Removing them can join the
            // end of one pasted field to the start of the next field and manufacture an
            // invalid address candidate.
            if (category == UnicodeCategory.Format)
                continue;

            if (char.IsWhiteSpace(character) ||
                category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                builder.Append(' ');
                continue;
            }

            if (category == UnicodeCategory.Control)
                continue;

            builder.Append(character);
        }

        var cleaned = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        if (cleaned.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..].Trim();

        return cleaned;
    }

    private static bool TryCanonicalize(string candidate, out string normalized)
    {
        normalized = string.Empty;
        if (!MailAddress.TryCreate(candidate, out var parsed))
            return false;

        var address = parsed.Address;
        var separator = address.LastIndexOf('@');
        if (separator <= 0 || separator >= address.Length - 1)
            return false;

        var localPart = address[..separator];
        var domain = address[(separator + 1)..];

        try
        {
            domain = new IdnMapping().GetAscii(domain).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        var canonical = $"{localPart}@{domain}";
        if (!MailAddress.TryCreate(canonical, out var verified) ||
            !string.Equals(verified.Address, canonical, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = canonical;
        return true;
    }
}
