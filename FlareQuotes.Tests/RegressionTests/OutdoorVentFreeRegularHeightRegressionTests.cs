using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class OutdoorVentFreeRegularHeightRegressionTests
{
    [Theory]
    [InlineData("VST70")]
    [InlineData("VFST70")]
    public async Task RegularVfst70UsesVentlessLinksAndPricesReflectiveSides(string model)
    {
        var root = FindRepoRoot();
        var pricingPath = Path.Combine(root, "LocalData", "pricing.xlsx");
        Assert.True(File.Exists(pricingPath), $"Pricing workbook missing: {pricingPath}");

        var reflectiveSides = new FeatureSelection {
            Key = "reflective_black_sides",
            DisplayName = "Reflective Black Sides",
            PdfDescription = "Black Glass Sides That Reflects the Flame and Media"
        };

        var fireplace = new FireplaceQuote {
            Type = FireplaceType.OutdoorSeeThrough,
            Model = model,
            Size = "70",
            GlassHeight = "16",
            LeadTime = "3-5 Business Days",
            Features = [reflectiveSides]
        };

        var request = new QuoteRequest {
            ProjectName = "VFST70 Regression",
            ClientName = "Flare QA",
            Email = "test@example.com",
            Model = model,
            Size = "70",
            GlassHeight = "16",
            Fireplaces = [fireplace]
        };

        var priceBook = new ClosedXmlPriceBookService();
        var priced = await priceBook.BuildPricedQuoteAsync(request, pricingPath);
        var pricedFireplace = Assert.Single(priced.Fireplaces);
        var reflectiveLine = Assert.Single(
            pricedFireplace.OptionalFeatures,
            line => line.Feature.Equals("Reflective Black Sides", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("VFRBSST", reflectiveLine.Sku);
        Assert.Equal(208m, reflectiveLine.Price);

        var resourceSets = await priceBook.ResolveResourceLinksAsync(request, pricingPath);
        var resourceSet = Assert.Single(resourceSets);

        Assert.Equal("VST-70", resourceSet.ModelNumber);
        Assert.NotEmpty(resourceSet.Links);

        foreach (var url in resourceSet.Links.Values)
        {
            Assert.Contains("/Data/Ventless/ST/", url);
            Assert.DoesNotContain("/Data/OD/", url);
        }
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
