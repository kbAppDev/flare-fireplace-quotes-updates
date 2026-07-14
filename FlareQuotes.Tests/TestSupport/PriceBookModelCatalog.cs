using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;

namespace FlareQuotes.Tests.TestSupport;

internal static partial class PriceBookModelCatalog
{
    public static IReadOnlyList<PriceRow> GetFireplaceModels(PriceBookWorkbook workbook)
    {
        return workbook.Rows.Where(row => row.Price.HasValue && FireplaceSkuRegex().IsMatch(row.Sku ?? string.Empty))
            .GroupBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => Category(row.Sku), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static FireplaceType Type(string? sku)
    {
        var value = (sku ?? string.Empty).Trim().ToUpperInvariant();

        if (value.StartsWith("VFST", StringComparison.Ordinal))
            return FireplaceType.OutdoorSeeThrough;

        if (value.StartsWith("VF", StringComparison.Ordinal))
            return FireplaceType.Outdoor;

        if (value.StartsWith("LDV", StringComparison.Ordinal))
            return FireplaceType.Large;

        if (value.StartsWith("DVTRA", StringComparison.Ordinal))
            return FireplaceType.Traditional;

        if (value.StartsWith("DVST", StringComparison.Ordinal) || value.Equals("DVPAST", StringComparison.Ordinal))
        {
            return FireplaceType.IndoorSeeThrough;
        }

        return FireplaceType.Indoor;
    }

    public static string Category(string? sku) => Type(sku) switch {
        FireplaceType.OutdoorSeeThrough => "Outdoor See Through",
        FireplaceType.Outdoor => "Outdoor",
        FireplaceType.Large => "Large",
        FireplaceType.Traditional => "Traditional",
        FireplaceType.IndoorSeeThrough => "Indoor See Through",
        _ => "Indoor"
    };

    [GeneratedRegex(@"^(?:DV(?:FF|ST|LC|RC|DC|RD)\d{2,3}(?:R|H|E)(?:C)?|DVTRA\d{2,3}|LDV(?:FF|ST|LC|RC|DC)\d{2,3}(?:" +
                    @"R|H|E)?|VF(?:FF|ST|LC|RC|DC)\d{2,3}(?:H)?|DVPA(?:FF|ST))$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FireplaceSkuRegex();
}
