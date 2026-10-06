using System.Text.RegularExpressions;

namespace FlareQuotes.Core.Models;

public static class TraditionalFireplaceModel
{
    public static bool IsBonfire(string? model)
    {
        var normalized = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", " ").Trim();
        var compact = normalized.Replace(" ", string.Empty);
        return Regex.IsMatch(compact, @"^(?:FLARE)?(?:TRABON|TRBON|BONTR|BONTRA)(?:42|46)?$",
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
               (Regex.IsMatch(normalized, @"\btraditional\b", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(normalized, @"\bbonfire\b|\bbon\b", RegexOptions.IgnoreCase));
    }
}
