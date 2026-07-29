using System.Text.RegularExpressions;

namespace FlareQuotes.Core.Models;

public static class QuoteProjectTitleFormatter
{
    public static string BuildFallback(IReadOnlyList<PricedFireplaceQuote>? fireplaces)
    {
        if (fireplaces is null || fireplaces.Count == 0)
            return string.Empty;

        return string.Join(" | ", fireplaces.Select(BuildFireplaceTitle));
    }

    public static string BuildFireplaceTitle(PricedFireplaceQuote fireplace)
    {
        ArgumentNullException.ThrowIfNull(fireplace);

        var application = fireplace.Type switch
        {
            FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough => "Outdoor",
            FireplaceType.IndoorOutdoorSeeThrough => "Indoor-Outdoor",
            FireplaceType.Large => "Large",
            FireplaceType.Traditional => "Indoor",
            _ => "Indoor"
        };

        var style = ResolveStyle(fireplace);
        var width = CleanDimension(fireplace.Size);
        var height = CleanDimension(fireplace.GlassHeight);

        var title = string.Join(" ", new[] { application, style }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
            return $"{title} {width}\" x {height}\"";
        if (!string.IsNullOrWhiteSpace(width))
            return $"{title} {width}\"";
        return title;
    }

    private static string ResolveStyle(PricedFireplaceQuote fireplace)
    {
        var value = string.Join(" ", fireplace.Model, fireplace.Description, fireplace.FireplaceLabel);
        var normalized = Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]+", " ");

        if (normalized.Contains("ROOM DEFINER") || Regex.IsMatch(normalized, @"\bRD\b"))
            return "Room Definer";
        if (normalized.Contains("DOUBLE CORNER") || Regex.IsMatch(normalized, @"\bDC\b"))
            return "Double Corner";
        if (normalized.Contains("LEFT CORNER") || Regex.IsMatch(normalized, @"\bLC\b"))
            return "Left Corner";
        if (normalized.Contains("RIGHT CORNER") || Regex.IsMatch(normalized, @"\bRC\b"))
            return "Right Corner";
        if (normalized.Contains("SEE THROUGH") || Regex.IsMatch(normalized, @"\bST\b"))
            return "See Through";
        if (normalized.Contains("TRADITIONAL") || Regex.IsMatch(normalized, @"\bTRA\b"))
            return "Traditional";
        return "Front Facing";
    }

    private static string CleanDimension(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+(?:\.\d+)?");
        return match.Success ? match.Value : string.Empty;
    }
}
