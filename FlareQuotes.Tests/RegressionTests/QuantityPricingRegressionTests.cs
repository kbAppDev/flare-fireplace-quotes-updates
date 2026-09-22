using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class QuantityPricingRegressionTests
{
    [Fact]
    public async Task ThreeIdenticalFireplacesScaleBaseFeaturesMediaAndTotalExactly()
    {
        var pricingPath = Path.Combine(FindRepoRoot(), "LocalData", "pricing.xlsx");
        Assert.True(File.Exists(pricingPath), $"Pricing workbook missing: {pricingPath}");

        var service = new ClosedXmlPriceBookService();
        var singleResult = await service.BuildPricedQuoteAsync(BuildRequest(quantity: 1), pricingPath);
        var tripleResult = await service.BuildPricedQuoteAsync(BuildRequest(quantity: 3), pricingPath);

        Assert.True(singleResult.Success, singleResult.Message);
        Assert.True(tripleResult.Success, tripleResult.Message);

        var single = Assert.Single(singleResult.Fireplaces);
        var triple = Assert.Single(tripleResult.Fireplaces);

        Assert.Equal(1, single.Quantity);
        Assert.Equal(3, triple.Quantity);
        Assert.Equal(1, single.BaseLine.Quantity);
        Assert.Equal(3, triple.BaseLine.Quantity);
        AssertExtendedPriceScales(single.BaseLine, triple.BaseLine, multiplier: 3);

        Assert.Equal(single.OptionalFeatures.Count, triple.OptionalFeatures.Count);
        Assert.NotEmpty(single.OptionalFeatures);

        foreach (var singleLine in single.OptionalFeatures)
        {
            var tripleLine = Assert.Single(
                triple.OptionalFeatures,
                candidate => string.Equals(candidate.Feature, singleLine.Feature, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(candidate.Sku, singleLine.Sku, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(singleLine.Quantity * 3, tripleLine.Quantity);
            AssertExtendedPriceScales(singleLine, tripleLine, multiplier: 3);
        }

        var singlePowerVent = Assert.Single(
            single.OptionalFeatures,
            line => line.Feature.Equals("Power Vent", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, singlePowerVent.Quantity);
        Assert.True(singlePowerVent.Price > 0m, "Power Vent must have a workbook price for this regression.");

        var singleGoldGlass = Assert.Single(
            single.OptionalFeatures,
            line => line.Feature.Contains("Gold", StringComparison.OrdinalIgnoreCase) &&
                    line.Feature.Contains("Glass", StringComparison.OrdinalIgnoreCase));
        Assert.True(singleGoldGlass.Quantity > 1,
                    "The selected premium media must exercise its calculated per-fireplace quantity.");
        Assert.True(singleGoldGlass.Price > 0m, "Gold Glass must have a workbook price for this regression.");
        var tripleGoldGlass = Assert.Single(
            triple.OptionalFeatures,
            line => line.Feature.Contains("Gold", StringComparison.OrdinalIgnoreCase) &&
                    line.Feature.Contains("Glass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains($"{tripleGoldGlass.Quantity} sets", tripleGoldGlass.Description,
                        StringComparison.OrdinalIgnoreCase);

        Assert.Equal(single.TotalMsrp * 3m, triple.TotalMsrp);
        Assert.Equal(singleResult.TotalMsrp * 3m, tripleResult.TotalMsrp);
    }

    private static QuoteRequest BuildRequest(int quantity)
    {
        var fireplace = new FireplaceQuote
        {
            Quantity = quantity,
            Type = FireplaceType.Indoor,
            Model = "Front Facing",
            Size = "60",
            GlassHeight = "16",
            LeadTime = "3-5 Business Days",
            Features =
            [
                new FeatureSelection
                {
                    Key = "power_vent",
                    DisplayName = "Power Vent",
                    PdfDescription = "Run Up to 8 90-Degree Elbows and 100' of Venting"
                }
            ],
            PremiumMedia =
            [
                new MediaSelection
                {
                    Key = "gold_glass",
                    DisplayName = "Gold Glass",
                    IsPremium = true
                }
            ]
        };

        return new QuoteRequest
        {
            ProjectName = "Quantity Pricing Regression",
            ClientName = "Flare QA",
            Model = fireplace.Model,
            Size = fireplace.Size,
            GlassHeight = fireplace.GlassHeight,
            Fireplaces = [fireplace]
        };
    }

    private static void AssertExtendedPriceScales(PriceLine single, PriceLine triple, int multiplier)
    {
        Assert.True(single.Price.HasValue, $"Single-quantity price missing for {single.Feature} ({single.Sku}).");
        Assert.True(triple.Price.HasValue, $"Multi-quantity price missing for {triple.Feature} ({triple.Sku}).");
        Assert.Equal(single.Price.Value * multiplier, triple.Price.Value);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "FlareQuotes.App")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
