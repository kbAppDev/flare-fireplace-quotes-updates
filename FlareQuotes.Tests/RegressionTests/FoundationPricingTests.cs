using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class FoundationPricingTests
{
    private static string PricingPath() => Path.Combine(AppContext.BaseDirectory, "LocalData", "pricing.xlsx");

    [Fact]
    public async Task EveryActiveFoundationPriceLoadsAndDuplicateSkusAgree()
    {
        var workbook = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var expectedCounts = new Dictionary<string, int>
        {
            // The loader intentionally excludes 36 discontinued commercial fireplace rows.
            ["Indoor Price Book"] = 547,
            ["Outdoor Price Book"] = 209,
            ["Large Price Book"] = 134,
            ["Parts Price Book"] = 544
        };
        foreach (var (sheet, count) in expectedCounts)
        {
            var rows = workbook.Rows.Where(row => row.SheetName == sheet && !string.IsNullOrWhiteSpace(row.Sku)).ToList();
            Assert.Equal(count, rows.Count);
            Assert.All(rows, row => Assert.True(row.Price.HasValue && row.Price >= 0, $"Missing or invalid MSRP: {sheet}/{row.Sku}"));
        }

        var conflicts = workbook.Rows.Where(row => expectedCounts.ContainsKey(row.SheetName) && !string.IsNullOrWhiteSpace(row.Sku))
            .GroupBy(row => row.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(row => row.Price).Distinct().Count() != 1)
            .Select(group => group.Key);
        Assert.Empty(conflicts);
        Assert.Contains(workbook.Rows, row => row.PartName == "FLARE-LC-SS-H" && row.Sku == "1SCLCSH" && row.Price == 105m);
        Assert.Contains(workbook.Rows, row => row.PartName == "FLARE-LC-SS-EH" && row.Sku == "1SCLCSE" && row.Price == 242m);
    }

    [Fact]
    public async Task NewVentingAndBonfirePartsLoadWithTheirFoundationPrices()
    {
        var workbook = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var expected = new Dictionary<string, decimal>
        {
            ["811DVP-12"] = 150m,
            ["811DVP-18"] = 178m,
            ["811DVP-24"] = 206m,
            ["811DVP-36"] = 250m,
            ["811DVP-48"] = 300m,
            ["811DVP-ADJ"] = 234m,
            ["811DVP-AD-58-811"] = 323m,
            ["811DVP-AD-811-58"] = 323m,
            ["811DVP-E45"] = 211m,
            ["811DVP-E90"] = 334m,
            ["811DVP-SP"] = 223m,
            ["811DVP-ES"] = 72m,
            ["811DVP-WS"] = 89m,
            ["811DVP-HFS"] = 178m,
            ["811DVP-VFS"] = 178m,
            ["811DVP-WT"] = 278m,
            ["811DVP-AAIS"] = 389m,
            ["811DVP-HC-SS"] = 1558m,
            ["811DVP-VC-SS"] = 579m,
            ["811DVP-VS-SS"] = 267m,
            ["811DVP-VSK-SS"] = 267m,
            ["811DVP-F6"] = 234m,
            ["811DVP-F12"] = 306m,
            ["811DVP-FF"] = 250m,
            ["811DVP-SC"] = 61m,
            ["BNGRT"] = 75m
        };
        foreach (var (sku, price) in expected)
        {
            var row = Assert.Single(workbook.Rows, row => row.SheetName == "Parts Price Book" && row.Sku == sku);
            Assert.Equal(price, row.Price);
        }
        Assert.DoesNotContain(workbook.Rows, row => row.Sku is "BONGT42" or "BONGT46");
    }
}
