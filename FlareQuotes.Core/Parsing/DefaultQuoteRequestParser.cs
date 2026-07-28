using System.Text.RegularExpressions;
using FlareQuotes.Core.Email;
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
        request.Email = EmailAddressNormalizer.ExtractFirstOrEmpty(rawText);
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

        var decodedModel = TryDecodeCompleteFireplaceCode(
            FirstNonBlank(request.Model, FindCompleteFireplaceCode(rawText)));

        if (decodedModel is not null)
        {
            request.Model = decodedModel.Model;
            request.Size = FirstNonBlank(request.Size, decodedModel.Size);
            request.GlassHeight = FirstNonBlank(request.GlassHeight, decodedModel.GlassHeight);
        }

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

        // Examples: FF-80-H, FF80H, FF-80-EH, FF80EH, DVFF50HC.
        // EH is listed before E/H so the 30\" suffix wins correctly.
        var match = Regex.Match(text, @"(?i)\b[A-Z]{1,10}[-\s]*\d{2,3}[-\s]*(EH|E|H|R)(?:C)?\b");
        return match.Success ? NormalizeGlassHeight(match.Groups[1].Value) : string.Empty;
    }

    private static string FindCompleteFireplaceCode(string? value)
    {
        var text = value ?? string.Empty;

        foreach (var pattern in new[]
                 {
                     @"(?i)\bDV[-\s]*(?:FF|ST|LC|RC|DC|RD)[-\s]*\d{2,3}[-\s]*(?:EH|E|H|R)C?\b",
                     @"(?i)\b(?:VFFF|VFST|VFLC|VFRC|VFDC|VFF|VST|VLC|VRC|VDC)[-\s]*\d{2,3}(?:[-\s]*(?:EH|H|R))?\b",
                     @"(?i)\bLDV[-\s]*(?:FF|LC|RC|DC)[-\s]*\d{3}(?:[-\s]*H)?\b",
                     @"(?i)\bDVTRA[-\s]*\d{2,3}\b",
                     @"(?i)\bDVPA(?:FF|ST)\b"
                 })
        {
            var match = Regex.Match(text, pattern);
            if (match.Success)
                return match.Value;
        }

        return string.Empty;
    }

    private static DecodedFireplaceCode? TryDecodeCompleteFireplaceCode(string? value)
    {
        var compact = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();

        if (compact == "DVPAFF")
            return new DecodedFireplaceCode("FFPASS", "30", "60");

        if (compact == "DVPAST")
            return new DecodedFireplaceCode("STPASS", "30", "60");

        var traditional = Regex.Match(compact, @"^DVTRA(?<size>\d{2,3})$");
        if (traditional.Success)
            return new DecodedFireplaceCode("Traditional", traditional.Groups["size"].Value, string.Empty);

        var large = Regex.Match(compact, @"^LDV(?<style>FF|LC|RC|DC)(?<size>\d{3})(?<height>H)?$");
        if (large.Success)
        {
            return new DecodedFireplaceCode(
                $"Large {ReadableStyle(large.Groups["style"].Value)}",
                large.Groups["size"].Value,
                large.Groups["height"].Success ? "24" : string.Empty);
        }

        var indoor = Regex.Match(
            compact,
            @"^DV(?<style>FF|ST|LC|RC|DC|RD)(?<size>\d{2,3})(?<height>EH|E|H|R)(?<commercial>C)?$");

        if (indoor.Success)
        {
            var style = ReadableStyle(indoor.Groups["style"].Value);
            var model = indoor.Groups["commercial"].Success ? $"Commercial {style}" : style;

            return new DecodedFireplaceCode(
                model,
                indoor.Groups["size"].Value,
                NormalizeGlassHeight(indoor.Groups["height"].Value));
        }

        var outdoor = Regex.Match(
            compact,
            @"^(?<prefix>VFFF|VFST|VFLC|VFRC|VFDC|VFF|VST|VLC|VRC|VDC)(?<size>\d{2,3})(?<height>EH|H|R)?$");

        if (outdoor.Success)
        {
            var prefix = outdoor.Groups["prefix"].Value;
            var style = prefix switch
            {
                "VFST" or "VST" => "See Through",
                "VFLC" or "VLC" => "Left Corner",
                "VFRC" or "VRC" => "Right Corner",
                "VFDC" or "VDC" => "Double Corner",
                _ => "Front Facing"
            };
            var height = outdoor.Groups["height"].Success
                             ? NormalizeGlassHeight(outdoor.Groups["height"].Value)
                             : "16";

            return new DecodedFireplaceCode(
                $"Outdoor Vent Free {style}",
                outdoor.Groups["size"].Value,
                height);
        }

        return null;
    }

    private static string ReadableStyle(string style) => style switch
    {
        "ST" => "See Through",
        "LC" => "Left Corner",
        "RC" => "Right Corner",
        "DC" => "Double Corner",
        "RD" => "Room Definer",
        _ => "Front Facing"
    };

    private sealed record DecodedFireplaceCode(string Model, string Size, string GlassHeight);

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
        if (compact is "E" or "EH" || compact.Contains("EXTRAHIGH") || compact.Contains("30"))
            return "30";
        if (compact == "H" || compact.Contains("HIGH") || compact.Contains("24"))
            return "24";
        if (compact == "R" || compact.Contains("REGULAR") || compact.Contains("STANDARD") || compact.Contains("16"))
            return "16";

        var m = Regex.Match(text, @"\d+");
        return m.Success ? m.Value : text;
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
