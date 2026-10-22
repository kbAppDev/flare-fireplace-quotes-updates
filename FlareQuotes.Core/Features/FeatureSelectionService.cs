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

        var options = GetAvailableOptions(type);
        var matches = new List<FeatureAliasMatch>();
        foreach (var option in options)
        {
            foreach (var alias in option.Aliases.Prepend(option.DisplayName).Select(Normalize)
                         .Where(alias => alias.Length > 0).Distinct(StringComparer.Ordinal))
            {
                // Traditional interior aliases may be followed by their model size, including
                // compact workbook SKUs such as RDTRA46 and HBLSTRA42.
                var compactAlias = alias.Replace(" ", string.Empty);
                var sizeSuffix = Regex.IsMatch(compactAlias, @"^(?:hb)?(?:rb|rd|nt|bl|ls)tra$")
                                     ? @"(?: ?(?:42|46))?"
                                     : string.Empty;
                var pattern = $@"\b{Regex.Escape(alias)}{sizeSuffix}\b";
                foreach (Match match in Regex.Matches(haystack, pattern))
                    matches.Add(new FeatureAliasMatch(option.Key, match.Index, match.Length));
            }
        }

        // A generic alias such as "red brick" must not also select Offset Red Brick
        // inside "herringbone red brick". Keep separate occurrences elsewhere in the request.
        var matchedKeys = matches
            .Where(match => !matches.Any(other => other.Length > match.Length &&
                                                 other.Start <= match.Start &&
                                                 other.End >= match.End))
            .Select(match => match.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return options.Where(option => matchedKeys.Contains(option.Key))
            .DistinctBy(x => x.Key)
            .ToList();
    }

    private sealed record FeatureAliasMatch(string Key, int Start, int Length)
    {
        public int End => Start + Length;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
}
