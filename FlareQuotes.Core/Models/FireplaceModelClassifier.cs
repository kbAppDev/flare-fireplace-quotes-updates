using System.Text.RegularExpressions;

namespace FlareQuotes.Core.Models;

public static class FireplaceModelClassifier
{
    public static FireplaceType DetectType(string? model, string? size = null)
    {
        var compact = Compact(model);
        var normalized = Normalize(model);

        if (IsIndoorOutdoorSeeThroughCode(compact))
            return FireplaceType.IndoorOutdoorSeeThrough;
        if (IsInvalidLargeSeeThrough(compact, normalized, size))
            return FireplaceType.Unknown;
        if (IsPassage(compact))
            return IsSeeThroughPassage(compact) ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor;

        if (IsOutdoorVentFree(compact))
        {
            return compact.StartsWith("VST", StringComparison.Ordinal) ||
                   compact.StartsWith("VFST", StringComparison.Ordinal)
                       ? FireplaceType.OutdoorSeeThrough
                       : FireplaceType.Outdoor;
        }

        if (IsTraditionalCode(compact) || normalized.Contains("traditional") || normalized.Contains("dvtra") ||
            normalized.Contains("trabon") || normalized.Contains("tra bon") ||
            Regex.IsMatch(normalized, @"\btr\b|\btra\b|\btrad\b"))
        {
            return FireplaceType.Traditional;
        }

        if (TrySize(size, out var numericSize) && numericSize >= 120)
            return FireplaceType.Large;
        if (normalized.Contains("large") || normalized.Contains("long"))
            return FireplaceType.Large;

        var isSeeThrough = normalized.Contains("see through") || Regex.IsMatch(normalized, @"\bst\b") ||
                           normalized.Contains("st od");
        var isIndoorOutdoor = normalized.Contains("indoor outdoor") || normalized.Contains("indooroutdoor") ||
                              normalized.Contains("st od");
        var isVentFreeOutdoor = normalized.Contains("vent free") ||
                                Regex.IsMatch(normalized, @"\bvf\b|\bvff\b|\bvst\b");
        var isOutdoor = normalized.Contains("outdoor") || isVentFreeOutdoor;

        if (isSeeThrough && isIndoorOutdoor)
            return FireplaceType.IndoorOutdoorSeeThrough;
        if (isOutdoor)
            return isSeeThrough || normalized.Contains("vst")
                       ? FireplaceType.OutdoorSeeThrough
                       : FireplaceType.Outdoor;
        return isSeeThrough ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor;
    }

    private static bool IsIndoorOutdoorSeeThroughCode(string compact) =>
        Regex.IsMatch(compact, @"^ST(?:PASS)?(?:OD|IO)$", RegexOptions.IgnoreCase);

    private static bool IsPassage(string compact) =>
        compact is "FFPASS" or "PASSFF" or "STPASS" or "PASSST" ||
        Regex.IsMatch(compact, @"^STPASS(?:OD|IO)$", RegexOptions.IgnoreCase);

    private static bool IsSeeThroughPassage(string compact) =>
        compact is "STPASS" or "PASSST" || Regex.IsMatch(compact, @"^STPASS(?:OD|IO)$", RegexOptions.IgnoreCase);

    private static bool IsTraditionalCode(string compact) =>
        compact is "TR" or "TRA" || Regex.IsMatch(compact, @"^(?:TR|TRA)\d{2,3}$") ||
        compact.StartsWith("TRAD", StringComparison.Ordinal) || compact.Contains("TRADITIONAL", StringComparison.Ordinal);

    private static bool IsOutdoorVentFree(string compact) =>
        Regex.IsMatch(compact, @"^(?:VFF|VST|VLC|VRC|VDC|VFST|VFLC|VFRC|VFDC)", RegexOptions.IgnoreCase);

    private static bool IsInvalidLargeSeeThrough(string compact, string normalized, string? size)
    {
        var hasSize = TrySize(size, out var numericSize);
        if (!hasSize)
        {
            var match = Regex.Match(compact, @"(?:LDVST|ST)(\d{3})", RegexOptions.IgnoreCase);
            hasSize = match.Success && int.TryParse(match.Groups[1].Value, out numericSize);
        }

        if (!hasSize || numericSize < 120)
            return false;

        return compact.StartsWith("LDVST", StringComparison.Ordinal) ||
               Regex.IsMatch(compact, @"^ST\d{3}(?:R|H|EH)?$", RegexOptions.IgnoreCase) ||
               normalized.Contains("large see through") || normalized.Contains("large see-through");
    }

    private static bool TrySize(string? value, out int size)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+");
        return int.TryParse(match.Value, out size);
    }

    private static string Compact(string? value) =>
        Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}
