using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Core.Parsing;

public sealed class DefaultQuoteRequestParser : IQuoteRequestParser
{
    public QuoteRequest Parse(string rawText)
    {
        rawText ??= string.Empty;
        var request = new QuoteRequest { RawRequestText = rawText };

        request.ProjectName = FindValue(rawText, "Project Name", "Project");
        request.ClientName =
            FindValue(rawText, "Name", "Client Name", "Customer Name", "Customer", "Client", "Contact Name");
        request.Email =
            Regex.Match(rawText, @"[A-Z0-9._%+\-']+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase).Value;
        request.Phone = NormalizePhone(FindValue(rawText, "Phone", "Telephone", "Tel", "Cell", "Mobile"));
        if (string.IsNullOrWhiteSpace(request.Phone))
            request.Phone = NormalizePhone(
                Regex.Match(rawText, @"(?:\+?1[\s\-.]?)?(?:\(?\d{3}\)?[\s\-.]?)\d{3}[\s\-.]?\d{4}").Value);
        request.Postal = FindValue(rawText, "Postal", "Postal Code", "Zip", "ZIP Code");
        request.ProjectAddress = FindValue(rawText, "Project Address", "Address");
        request.InstallDate = FindValue(rawText, "Estimated Install Date", "Install Date");
        request.FireplaceLocation = FindValue(rawText, "Fireplace Location", "Location");
        request.Model = FindValue(rawText, "Model", "Fireplace Model", "Style");
        request.Size = CleanInches(FindValue(rawText, "Size", "Length"));
        request.GlassHeight =
            FirstNonBlank(NormalizeGlassHeight(FindValue(rawText, "Glass Height", "Height")),
                          ExtractGlassHeightFromModelCode(request.Model), ExtractGlassHeightFromModelCode(rawText));
        request.RawFeaturesText = FindValue(rawText, "Features", "Additional Features", "Options");

        if (string.IsNullOrWhiteSpace(request.ClientName))
        {
            request.ClientName = GuessClientName(rawText, request.Email);
        }

        return request;
    }

    private static string FindValue(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var match = Regex.Match(text, $@"(?im)^\s*{Regex.Escape(label)}\s*:\s*(.+?)\s*$");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }
        return string.Empty;
    }

    private static string CleanInches(string value)
    {
        var m = Regex.Match(value ?? string.Empty, @"\d+");
        return m.Success ? m.Value : (value ?? string.Empty).Trim();
    }

    private static string ExtractGlassHeightFromModelCode(string? value)
    {
        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Examples: FF-80-H, FF80H, FF-80-EH, FF80EH, DVDR45R.
        // EH is listed before H so the 30\" suffix wins correctly.
        var match = Regex.Match(text, @"(?i)\b[A-Z]{1,10}[-\s]*\d{2,3}[-\s]*(EH|H|R)\b");
        return match.Success ? NormalizeGlassHeight(match.Groups[1].Value) : string.Empty;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string NormalizeGlassHeight(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = Regex.Replace(text.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

        // Order matters: EH contains H, so Extra High must be checked first.
        if (compact == "EH" || compact.Contains("EXTRAHIGH") || compact.Contains("30"))
            return "30";
        if (compact == "H" || compact.Contains("HIGH") || compact.Contains("24"))
            return "24";
        if (compact == "R" || compact.Contains("REGULAR") || compact.Contains("STANDARD") || compact.Contains("16"))
            return "16";

        var m = Regex.Match(text, @"\d+");
        return m.Success ? m.Value : text;
    }

    private static string InferGlassHeightFromModelCode(string rawText, string model)
    {
        var source = $"{model}`n{rawText}".ToUpperInvariant();

        // Order matters: EH before H.
        if (Regex.IsMatch(source, @"\b(?:DV)?(?:FF|ST|LC|RC|DC|RD|VFF|VST|VLC|VRC|VDC|VF)[-\s]?\d{2,3}[-\s]?EH\b"))
            return "30";

        if (Regex.IsMatch(source, @"\b(?:DV)?(?:FF|ST|LC|RC|DC|RD|VFF|VST|VLC|VRC|VDC|VF)[-\s]?\d{2,3}[-\s]?H\b"))
            return "24";

        if (Regex.IsMatch(source, @"\b(?:DV)?(?:FF|ST|LC|RC|DC|RD|VFF|VST|VLC|VRC|VDC|VF)[-\s]?\d{2,3}[-\s]?R\b"))
            return "16";

        return string.Empty;
    }
    private static string NormalizePhone(string? value)
    {
        var digits = Regex.Replace(value ?? string.Empty, @"\D", "");
        if (digits.Length == 11 && digits.StartsWith("1"))
            digits = digits[1..];
        return digits.Length == 10 ? $"({digits[..3]}) {digits.Substring(3, 3)}-{digits[6..]}"
                                   : (value ?? string.Empty).Trim();
    }

    private static string GuessClientName(string text, string email)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 1)
                        .ToList();

        foreach (var line in lines)
        {
            if (line.Contains(':') || line.Contains('@') || Regex.IsMatch(line, @"\d{3}"))
                continue;
            if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 2 and <= 4)
                return line;
        }

        return string.Empty;
    }
}
