using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Core.Media;

public sealed class MediaSelectionService : IMediaSelectionService
{
    public IReadOnlyList<MediaOption> GetClassicMedia(FireplaceType type)
    {
        // Outdoor quotes use glass-style classic media only in the Python app.
        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            return MediaCatalog.Classic
                .Where(x => x.Key.StartsWith("fg_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Label)
                .ToList();
        }

        return MediaCatalog.Classic.OrderBy(x => x.Label).ToList();
    }

    public IReadOnlyList<MediaOption> GetPremiumMedia(FireplaceType type)
    {
        // Outdoor fireplace quotes should only offer premium glass media options.
        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            return MediaCatalog.Premium
                .Where(x => x.CalculationGroup.Equals("fireglass", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("glass", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Label)
                .ToList();
        }

        return MediaCatalog.Premium
            .Where(x => !x.Key.Equals("small_driftwood", StringComparison.OrdinalIgnoreCase) &&
                        !x.Key.Equals("large_driftwood", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Label)
            .ToList();
    }

    public IReadOnlyList<MediaOption> DetectFromText(string rawText, FireplaceType type)
    {
        var haystack = Normalize(rawText);
        if (string.IsNullOrWhiteSpace(haystack)) return [];

        return GetClassicMedia(type)
            .Concat(GetPremiumMedia(type))
            .Where(option => option.Aliases.Any(alias => Regex.IsMatch(haystack, $@"\b{Regex.Escape(Normalize(alias))}\b", RegexOptions.IgnoreCase)))
            .DistinctBy(x => x.Key)
            .ToList();
    }

    private static string Normalize(string value) => Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
}
