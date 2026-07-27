using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class FireplaceCatalogMatchingRegressionTests
{
    [Theory]
    [InlineData(
        FireplaceType.Indoor,
        "Commercial Front Facing",
        "80",
        "16",
        "DVFF80RC",
        "FF80")]
    [InlineData(
        FireplaceType.IndoorSeeThrough,
        "Commercial See Through",
        "80",
        "16",
        "DVST80RC",
        "ST80")]
    [InlineData(
        FireplaceType.Outdoor,
        "Left Corner",
        "100",
        "16",
        "VFLC100",
        "VLC100")]
    [InlineData(
        FireplaceType.Outdoor,
        "Double Corner",
        "100",
        "16",
        "VFDC100",
        "VDC100")]
    public async Task BasePricingAndSpecificationsUseTheExactCatalogFamily(
        FireplaceType type,
        string model,
        string size,
        string glassHeight,
        string expectedSku,
        string expectedSpecIdentity)
    {
        var root = FindRepoRoot();
        var pricingPath = Path.Combine(root, "LocalData", "pricing.xlsx");
        Assert.True(File.Exists(pricingPath), $"Pricing workbook missing: {pricingPath}");

        var service = new ClosedXmlPriceBookService();
        var workbook = await service.LoadAsync(pricingPath);
        var expectedRow = Assert.Single(
            workbook.Rows,
            row => string.Equals(row.Sku, expectedSku, StringComparison.OrdinalIgnoreCase));

        var fireplace = new FireplaceQuote
        {
            Type = type,
            Model = model,
            Size = size,
            GlassHeight = glassHeight,
            LeadTime = "AUTOMATED TEST"
        };

        var request = new QuoteRequest
        {
            ProjectName = $"Catalog Regression - {expectedSku}",
            ClientName = "Flare QA",
            Email = "catalog-regression@example.com",
            Model = model,
            Size = size,
            GlassHeight = glassHeight,
            Fireplaces = [fireplace]
        };

        var priced = await service.BuildPricedQuoteAsync(request, pricingPath);
        Assert.True(priced.Success, priced.Message);

        var pricedFireplace = Assert.Single(priced.Fireplaces);
        Assert.Equal(expectedSku, pricedFireplace.BaseLine.Sku, ignoreCase: true);
        Assert.Equal(expectedRow.Price, pricedFireplace.BaseLine.Price);
        Assert.False(string.IsNullOrWhiteSpace(pricedFireplace.Description));

        var resourceSet = Assert.Single(await service.ResolveResourceLinksAsync(request, pricingPath));
        var normalizedSpecModel = NormalizeKey(resourceSet.ModelNumber);

        Assert.Contains(
            NormalizeKey(expectedSpecIdentity),
            normalizedSpecModel,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(resourceSet.Links);

        foreach (var url in resourceSet.Links.Values)
        {
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"Invalid resource URL: {url}");
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }
    }

    private static string NormalizeKey(string? value) =>
        Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();

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
