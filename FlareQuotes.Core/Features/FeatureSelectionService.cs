using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Core.Features;

public sealed class FeatureSelectionService : IFeatureSelectionService
{
    public IReadOnlyList<FeatureOption> GetAvailableOptions(FireplaceType type)
    {
        return FeatureCatalog.All.Where(x => x.AppliesTo.Contains(type) || type == FireplaceType.Unknown).ToList();
    }

    public IReadOnlyList<FeatureOption> DetectFromText(string rawText, FireplaceType type)
    {
        var haystack = Normalize(rawText);
        if (string.IsNullOrWhiteSpace(haystack))
            return [];

        return GetAvailableOptions(type)
            .Where(option =>
                       option.Aliases.Any(alias => Regex.IsMatch(haystack, $@"\b{Regex.Escape(Normalize(alias))}\b",
                                                                 RegexOptions.IgnoreCase)))
            .DistinctBy(x => x.Key)
            .ToList();
    }

    private static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
}
