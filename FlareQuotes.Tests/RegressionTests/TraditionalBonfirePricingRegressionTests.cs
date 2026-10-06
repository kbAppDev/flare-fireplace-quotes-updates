using ClosedXML.Excel;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class TraditionalBonfirePricingRegressionTests
{
    [Theory]
    [InlineData("TRA-BON-42", "", "42", "TRABON42", 11313)]
    [InlineData("TRA-BON-46", "", "46", "TRABON46", 13574)]
    [InlineData("TRABON42", "", "42", "TRABON42", 11313)]
    [InlineData("BONTR46", "", "46", "TRABON46", 13574)]
    [InlineData("Traditional Bonfire", "42", "42", "TRABON42", 11313)]
    [InlineData("Traditional Bonfire", "46", "46", "TRABON46", 13574)]
    public async Task BonfireUsesItsOwnBaseIdentityAndInfersSizeFromCompleteCodes(
        string model, string inputSize, string expectedSize, string expectedSku, int expectedPrice)
    {
        var service = new ClosedXmlPriceBookService();
        var result = await service.BuildPricedQuoteAsync(Request(model, inputSize), PricingPath());

        Assert.True(result.Success, result.Message);
        var priced = Assert.Single(result.Fireplaces);
        Assert.Equal(FireplaceType.Traditional, priced.Type);
        Assert.Equal(expectedSize, priced.Size);
        Assert.Equal(expectedSku, priced.BaseLine.Sku);
        Assert.Equal((decimal)expectedPrice, priced.BaseLine.Price);
        Assert.Equal($"TRA-BON-{expectedSize}", priced.ModelNumber);
        Assert.Contains("Flat Bonfire Burner", priced.Description);
        Assert.Equal(priced.Description, priced.BaseLine.Description);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("46")]
    public async Task BonfireRetainsEveryTraditionalFeatureAtTheOriginalSize(string size)
    {
        var features = new FeatureSelectionService().GetAvailableOptions(FireplaceType.Traditional)
            .Select(option => new FeatureSelection
            {
                Key = option.Key,
                DisplayName = option.DisplayName,
                PdfDescription = option.PdfDescription
            }).ToList();
        var standard = Request("Traditional", size, features: features);
        var bonfire = Request("Traditional Bonfire", size, features: features);
        var service = new ClosedXmlPriceBookService();

        var standardResult = await service.BuildPricedQuoteAsync(standard, PricingPath());
        var bonfireResult = await service.BuildPricedQuoteAsync(bonfire, PricingPath());

        Assert.True(standardResult.Success, standardResult.Message);
        Assert.True(bonfireResult.Success, bonfireResult.Message);
        var standardPriced = Assert.Single(standardResult.Fireplaces);
        var bonfirePriced = Assert.Single(bonfireResult.Fireplaces);
        Assert.Equal($"DVTRA{size}", standardPriced.BaseLine.Sku);
        Assert.Equal($"TRABON{size}", bonfirePriced.BaseLine.Sku);
        Assert.Equal(features.Count, bonfirePriced.OptionalFeatures.Count);
        for (var index = 0; index < features.Count; index++)
            AssertSameMediaLine(standardPriced.OptionalFeatures[index], bonfirePriced.OptionalFeatures[index]);
    }

    [Theory]
    [InlineData("42", 1)]
    [InlineData("46", 1)]
    [InlineData("42", 3)]
    [InlineData("46", 3)]
    public async Task AllLooseMediaUsesThe45InchCalculationAndScalesWithFireplaceQuantity(string size, int quantity)
    {
        var media = MediaCatalog.Classic.Concat(MediaCatalog.Premium)
            .Where(option => !option.Key.StartsWith("tr", StringComparison.OrdinalIgnoreCase) &&
                             !option.Key.StartsWith("loak", StringComparison.OrdinalIgnoreCase))
            .Select(option => new MediaSelection
            {
                Key = option.Key,
                DisplayName = option.DisplayName,
                IsPremium = option.IsPremium
            }).ToList();
        var service = new ClosedXmlPriceBookService();
        var expected = await service.BuildPricedQuoteAsync(
            Request("FF", "45", FireplaceType.Indoor, media: media, quantity: quantity), PricingPath());
        var actual = await service.BuildPricedQuoteAsync(
            Request("Traditional Bonfire", size, media: media, quantity: quantity), PricingPath());

        Assert.True(expected.Success, expected.Message);
        Assert.True(actual.Success, actual.Message);
        var expectedLines = Assert.Single(expected.Fireplaces).OptionalFeatures;
        var actualFireplace = Assert.Single(actual.Fireplaces);
        Assert.Equal(size, actualFireplace.Size);
        Assert.Equal($"TRA-BON-{size}", actualFireplace.ModelNumber);
        Assert.Equal(media.Count, actualFireplace.OptionalFeatures.Count);
        for (var index = 0; index < media.Count; index++)
            AssertSameMediaLine(expectedLines[index], actualFireplace.OptionalFeatures[index]);

        var goldGlass = Assert.Single(actualFireplace.OptionalFeatures,
            line => line.Feature.Equals("Gold Glass", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2 * quantity, goldGlass.Quantity);
        var chestnutGlass = Assert.Single(actualFireplace.OptionalFeatures,
            line => line.Sku.Equals("PMDFGC", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2 * quantity, chestnutGlass.Quantity);
        Assert.Equal(160m * quantity, chestnutGlass.Price);
        var stones = Assert.Single(actualFireplace.OptionalFeatures,
            line => line.Feature.Equals("Black Stones", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5 * quantity, stones.Quantity);
        var mixedBalls = Assert.Single(actualFireplace.OptionalFeatures,
            line => line.Feature.Equals("Mixed 2\" / 4\" Cape Grey Stone Balls", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"PSBGM-45", mixedBalls.Sku);
        Assert.Equal(257m * quantity, mixedBalls.Price);
        Assert.Contains("20-2\" / 8-4\" pcs", mixedBalls.Description);
        var driftwood = Assert.Single(actualFireplace.OptionalFeatures,
            line => line.Feature.Equals("Driftwood", StringComparison.OrdinalIgnoreCase));
        Assert.Contains($"{quantity} Large", driftwood.Description);
        Assert.DoesNotContain("Small", driftwood.Description);
    }

    [Theory]
    [InlineData("42", "tr42bch", "Small Birch Logs for Traditional 42\"", "TR42BCH", 208)]
    [InlineData("46", "tr46bch", "Small Birch Logs for Traditional 46\"", "TR46BCH", 312)]
    public async Task StandardTraditionalBirchSetsRetainTheirOriginalPricesAndQuantities(
        string size, string key, string displayName, string expectedSku, int expectedPrice)
    {
        var service = new ClosedXmlPriceBookService();
        var result = await service.BuildPricedQuoteAsync(
            Request("Traditional", size,
                media: [new MediaSelection { Key = key, DisplayName = displayName, IsPremium = true }]),
            PricingPath());

        Assert.True(result.Success, result.Message);
        var fireplace = Assert.Single(result.Fireplaces);
        Assert.Equal($"DVTRA{size}", fireplace.BaseLine.Sku);
        Assert.Equal($"TR-{size}", fireplace.ModelNumber);
        var line = Assert.Single(fireplace.OptionalFeatures);
        Assert.Equal(expectedSku, line.Sku);
        Assert.Equal(1, line.Quantity);
        Assert.Equal((decimal)expectedPrice, line.Price);
    }

    [Theory]
    [InlineData("42", "loak42", "Large Oak Log Set for Traditional 42\"", "LOAK42")]
    [InlineData("46", "loak46", "Large Oak Log Set for Traditional 46\"", "LOAK46")]
    public async Task CompleteTraditionalLogSetsStayOneSetPerFireplace(
        string size, string key, string displayName, string expectedSku)
    {
        var service = new ClosedXmlPriceBookService();
        var result = await service.BuildPricedQuoteAsync(
            Request("Traditional Bonfire", size,
                media: [new MediaSelection { Key = key, DisplayName = displayName, IsPremium = true }], quantity: 3),
            PricingPath());

        Assert.True(result.Success, result.Message);
        var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
        Assert.Equal(expectedSku, line.Sku);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(534m * 3m, line.Price);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("46")]
    public async Task BonfireGetsItsProductSheetAndSharesEngineeringResourcesWithoutChangingStandardSets(string size)
    {
        var service = new ClosedXmlPriceBookService();
        var standardBefore = Assert.Single(await service.ResolveResourceLinksAsync(Request("Traditional", size), PricingPath()));
        var bonfire = Assert.Single(await service.ResolveResourceLinksAsync(Request($"TRA-BON-{size}", ""), PricingPath()));
        var standardAfter = Assert.Single(await service.ResolveResourceLinksAsync(Request("Traditional", size), PricingPath()));

        Assert.Equal($"TR-{size}", standardBefore.ModelNumber);
        Assert.Equal($"TRA-BON-{size}", bonfire.ModelNumber);
        Assert.Equal($"TR-{size}", standardAfter.ModelNumber);
        Assert.Equal($"https://flarefireplaces.com/wp-content/uploads/Data/TR/specs-TR{size}BON.pdf",
                     bonfire.Links["Product Sheet"]);
        Assert.Equal("specific", bonfire.Sources["Product Sheet"]);
        Assert.Equal(standardBefore.Links.Count + 1, bonfire.Links.Count);
        Assert.Equal(standardBefore.Links.Count, standardAfter.Links.Count);
        Assert.DoesNotContain("Product Sheet", standardAfter.Links.Keys);
        foreach (var resource in standardBefore.Links)
        {
            Assert.Equal(resource.Value, bonfire.Links[resource.Key]);
            Assert.Equal(resource.Value, standardAfter.Links[resource.Key]);
            Assert.Equal("specific", bonfire.Sources[resource.Key]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingTraditionalVariantNeverBorrowsTheOtherVariantsBasePrice(bool requestBonfire)
    {
        using var fixture = new TemporaryWorkbook();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Indoor Price Book");
            sheet.Cell(1, 1).Value = "Part Name";
            sheet.Cell(1, 2).Value = "Description";
            sheet.Cell(1, 3).Value = "SKU";
            sheet.Cell(1, 4).Value = "MSRP";
            sheet.Cell(2, 1).Value = requestBonfire ? "FLARE-TRA-42" : "TRA-BON-42";
            sheet.Cell(2, 2).Value = requestBonfire
                ? "Flare Traditional 42\" Fireplace"
                : "Flare Traditional 42\" Fireplace with Flat Bonfire Burner";
            sheet.Cell(2, 3).Value = requestBonfire ? "DVTRA42" : "TRABON42";
            sheet.Cell(2, 4).Value = 11313;
            var accessories = new[]
            {
                ("LRG-OAK-42", "Large oak log set for Traditional 42\"", "LOAK42", 534),
                ("CLG-TRA-42", "Double Glass - Passive for Traditional 42\" Fireplace", "CLGTRA42", 2018),
                ("RD-TRA-42", "Red Brick Traditional 42", "RDTRA42", 425),
                ("BON-KIT-42", "Traditional 42\" Fireplace Bonfire Burner Retrofit Kit", "BONKIT42", 1250),
                ("COVER-TRA-42", "Cover for Traditional 42\" Fireplace", "COVERTRA42", 150),
                ("ACC-TRA-42", "Traditional 42\" Fireplace Accessory", "ACCTRA42", 99),
                ("BON-ACC-42", "Traditional 42\" Bonfire Fireplace Accessory", "BONACC42", 199)
            };
            for (var index = 0; index < accessories.Length; index++)
            {
                var row = index + 3;
                sheet.Cell(row, 1).Value = accessories[index].Item1;
                sheet.Cell(row, 2).Value = accessories[index].Item2;
                sheet.Cell(row, 3).Value = accessories[index].Item3;
                sheet.Cell(row, 4).Value = accessories[index].Item4;
            }
            workbook.SaveAs(fixture.Path);
        }

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
            Request(requestBonfire ? "Traditional Bonfire" : "Traditional", "42"), fixture.Path);

        Assert.False(result.Success);
        Assert.Null(Assert.Single(result.Fireplaces).BaseLine.Price);
        Assert.Contains("base fireplace", result.Message);
    }

    [Theory]
    [InlineData(false, "FLARE-TRA-42", "")]
    [InlineData(false, "TRA-42", "")]
    [InlineData(false, "TR-42", "")]
    [InlineData(false, "DVTRA42", "")]
    [InlineData(true, "TRA-BON-42", "")]
    [InlineData(true, "BONTR42", "")]
    [InlineData(true, "TRBON42", "")]
    [InlineData(true, "BONTRA42", "")]
    [InlineData(false, "Custom Fireplace", "Flare Traditional 42\" Fireplace")]
    [InlineData(true, "Custom Fireplace", "Flare Traditional 42\" Fireplace with Flat Bonfire Burner")]
    public async Task CustomTraditionalBaseRowsRemainSupportedWithoutSku(
        bool isBonfire, string partName, string description)
    {
        using var fixture = new TemporaryWorkbook();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Indoor Price Book");
            sheet.Cell(1, 1).Value = "Part Name";
            sheet.Cell(1, 2).Value = "Description";
            sheet.Cell(1, 3).Value = "SKU";
            sheet.Cell(1, 4).Value = "MSRP";
            sheet.Cell(2, 1).Value = partName;
            sheet.Cell(2, 2).Value = description;
            sheet.Cell(2, 4).Value = 11313;
            workbook.SaveAs(fixture.Path);
        }

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
            Request(isBonfire ? "Traditional Bonfire" : "Traditional", "42"), fixture.Path);

        Assert.True(result.Success, result.Message);
        var fireplace = Assert.Single(result.Fireplaces);
        Assert.Equal(11313m, fireplace.BaseLine.Price);
        Assert.Equal(isBonfire ? "TRA-BON-42" : "TR-42", fireplace.ModelNumber);
    }

    [Fact]
    public async Task ExactBonfireResourceRowsWinOverSharedTraditionalRows()
    {
        using var fixture = new TemporaryWorkbook();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Resource Links");
            sheet.Cell(1, 1).Value = "Template";
            sheet.Cell(1, 2).Value = "Model #";
            sheet.Cell(1, 3).Value = "Style";
            sheet.Cell(1, 4).Value = "Size";
            sheet.Cell(1, 5).Value = "CAD";
            sheet.Cell(2, 1).Value = "Traditional";
            sheet.Cell(2, 2).Value = "TR-42";
            sheet.Cell(2, 3).Value = "Traditional";
            sheet.Cell(2, 4).Value = "42";
            sheet.Cell(2, 5).Value = "https://example.com/standard.dwg";
            sheet.Cell(3, 1).Value = "Traditional";
            sheet.Cell(3, 2).Value = "TRA-BON-42";
            sheet.Cell(3, 3).Value = "Traditional Bonfire";
            sheet.Cell(3, 4).Value = "42";
            sheet.Cell(3, 5).Value = "https://example.com/bonfire.dwg";
            workbook.SaveAs(fixture.Path);
        }

        var service = new ClosedXmlPriceBookService();
        var bonfire = Assert.Single(await service.ResolveResourceLinksAsync(Request("Traditional Bonfire", "42"), fixture.Path));
        var standard = Assert.Single(await service.ResolveResourceLinksAsync(Request("Traditional", "42"), fixture.Path));

        Assert.Equal("TRA-BON-42", bonfire.ModelNumber);
        Assert.Equal("https://example.com/bonfire.dwg", bonfire.Links["CAD"]);
        Assert.Equal("TR-42", standard.ModelNumber);
        Assert.Equal("https://example.com/standard.dwg", standard.Links["CAD"]);
    }

    private static void AssertSameMediaLine(PriceLine expected, PriceLine actual)
    {
        Assert.Equal(expected.Feature, actual.Feature);
        Assert.Equal(expected.Sku, actual.Sku);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Quantity, actual.Quantity);
        Assert.Equal(expected.Price, actual.Price);
    }

    private static QuoteRequest Request(
        string model, string size, FireplaceType type = FireplaceType.Unknown,
        List<FeatureSelection>? features = null, List<MediaSelection>? media = null, int quantity = 1) =>
        new()
        {
            Fireplaces =
            [
                new FireplaceQuote
                {
                    Type = type,
                    Model = model,
                    Size = size,
                    GlassHeight = type == FireplaceType.Indoor ? "16" : string.Empty,
                    Features = features ?? [],
                    PremiumMedia = media ?? [],
                    Quantity = quantity
                }
            ]
        };

    private static string PricingPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = System.IO.Path.Combine(directory.FullName, "LocalData", "pricing.xlsx");
            if (Directory.Exists(System.IO.Path.Combine(directory.FullName, "FlareQuotes.App")) && File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository pricing workbook not found.");
    }

    private sealed class TemporaryWorkbook : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlareBonfireTests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "pricing.xlsx");

        public TemporaryWorkbook() => Directory.CreateDirectory(_directory);
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
