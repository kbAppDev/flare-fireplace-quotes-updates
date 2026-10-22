using System.Reflection;
using ClosedXML.Excel;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class TraditionalOptionsPricingTests
{
    [Theory]
    [InlineData("TR42", "42")]
    [InlineData("TR46", "46")]
    [InlineData("TRBON42", "42")]
    [InlineData("TRBON46", "46")]
    public async Task AllEightBrickFinishesAndReflectiveInteriorUseTheirExactSizeAndMsrp(string model, string size)
    {
        (string Key, string SkuPrefix)[] interiors =
        [
            ("offset_red_brick_traditional", "RDTRA"),
            ("offset_natural_brick_traditional", "NTTRA"),
            ("offset_black_brick_traditional", "BLTRA"),
            ("offset_light_stone_brick_traditional", "LSTRA"),
            ("herringbone_red_brick_traditional", "HBRDTRA"),
            ("herringbone_natural_brick_traditional", "HBNTTRA"),
            ("herringbone_black_brick_traditional", "HBBLTRA"),
            ("herringbone_light_stone_brick_traditional", "HBLSTRA"),
            ("reflective_black_interior", "RBTRA")
        ];
        var request = new QuoteRequest();
        foreach (var (key, _) in interiors)
        {
            var option = Assert.Single(FeatureCatalog.All, item => item.Key == key);
            request.Fireplaces.Add(new FireplaceQuote
            {
                Type = FireplaceType.Traditional,
                Model = model,
                Size = size,
                Features = [new FeatureSelection
                {
                    Key = option.Key, DisplayName = option.DisplayName, PdfDescription = option.PdfDescription
                }]
            });
        }

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());
        Assert.True(result.Success, result.Message);
        Assert.Equal(interiors.Length, result.Fireplaces.Count);
        for (var index = 0; index < interiors.Length; index++)
        {
            var fireplace = result.Fireplaces[index];
            Assert.Equal((model.Contains("BON", StringComparison.Ordinal) ? "TRABON" : "DVTRA") + size,
                fireplace.BaseLine.Sku);
            var line = Assert.Single(fireplace.OptionalFeatures);
            Assert.Equal(interiors[index].SkuPrefix + size, line.Sku);
            var expectedPrice = interiors[index].SkuPrefix == "RBTRA"
                ? (size == "42" ? 972m : 1168m)
                : (size == "42" ? 425m : 483m);
            Assert.Equal(expectedPrice, line.Price);
            Assert.Equal("Indoor Price Book", line.SourceSheet);
        }
    }

    [Theory]
    [InlineData("TR42", "42")]
    [InlineData("TR46", "46")]
    [InlineData("TRBON42", "42")]
    [InlineData("TRBON46", "46")]
    public async Task RemainingTraditionalOptionsUseTheirApprovedPartsAndMsrp(string model, string size)
    {
        (string Key, string Sku, decimal Msrp)[] options =
        [
            ("double_glass", "CLGTRA" + size, size == "42" ? 2018m : 2537m),
            ("summer_kit", "100SK", 1776m),
            ("power_vent", "100IPV", 3061m),
            ("passive_heat_flex", "HFPK100", 1040m),
            ("active_heat_flex", "HFAK2570I", 2477m),
            ("heat_release_louver", "LUV140", 466.67m),
            ("air_intake_louver", "LUV70", 333.33m),
            ("analog_wall_timer", "AWALLTIMER", 89m)
        ];
        var request = new QuoteRequest();
        foreach (var (key, _, _) in options)
        {
            var option = Assert.Single(FeatureCatalog.All, item => item.Key == key);
            request.Fireplaces.Add(new FireplaceQuote
            {
                Type = FireplaceType.Traditional,
                Model = model,
                Size = size,
                Features = [new FeatureSelection
                {
                    Key = option.Key, DisplayName = option.DisplayName, PdfDescription = option.PdfDescription
                }]
            });
        }

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());
        Assert.True(result.Success, result.Message);
        Assert.Equal(options.Length, result.Fireplaces.Count);
        for (var index = 0; index < options.Length; index++)
        {
            var line = Assert.Single(result.Fireplaces[index].OptionalFeatures);
            Assert.Equal(options[index].Sku, line.Sku);
            Assert.Equal(options[index].Msrp, decimal.Round(line.Price!.Value, 2));
        }
    }

    [Theory]
    [InlineData("42", 425)]
    [InlineData("46", 483)]
    public async Task OffsetLightStoneWithoutBrickInItsNameUsesTheOffsetSkuAndScalesWithQuantity(string size, int unitMsrp)
    {
        var request = Request("TRBON" + size, size, "Offset Light Stone Traditional");
        request.Fireplaces[0].Quantity = 3;
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(result.Success, result.Message);
        var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
        Assert.Equal("LSTRA" + size, line.Sku);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(unitMsrp * 3m, line.Price);
    }

    [Fact]
    public void BrickLookupPrefersExactSkuOverAmbiguousDescriptionAndKeepsPatternsSeparate()
    {
        var workbook = new PriceBookWorkbook();
        var misleading = new PriceRow
        {
            SheetName = "Indoor Price Book",
            Description = "Light Stone Traditional 46",
            Price = 999m
        };
        var offset = new PriceRow
        {
            SheetName = "Indoor Price Book",
            Sku = "LSTRA46",
            Description = "Interior finish",
            Price = 483m
        };
        var herringbone = new PriceRow
        {
            SheetName = "Indoor Price Book",
            Sku = "HBLSTRA46",
            Description = "Herringbone Light Stone Brick Traditional 46",
            Price = 483m
        };
        workbook.Rows.AddRange([misleading, herringbone, offset]);
        Assert.Same(offset, FindFeature(workbook, "46", "Offset Light Stone Brick Traditional"));
        Assert.Same(herringbone, FindFeature(workbook, "46", "Herringbone Light Stone Brick Traditional"));
        workbook.Rows.Remove(offset);
        Assert.Same(misleading, FindFeature(workbook, "46", "Offset Light Stone Brick Traditional"));
        workbook.Rows.Remove(misleading);
        Assert.Null(FindFeature(workbook, "46", "Offset Light Stone Brick Traditional"));
        Assert.Null(FindFeature(workbook, "42", "Herringbone Light Stone Brick Traditional"));
    }

    [Theory]
    [InlineData("HBLSTRA46", "")]
    [InlineData("LSTRA42", "")]
    [InlineData("BLTRA46", "")]
    [InlineData("RBTRA46", "")]
    [InlineData("", "HB-LS-TRA-46")]
    [InlineData("", "LS-TRA-42")]
    public void BrickDescriptionCannotOverrideAnotherTraditionalPartOrSku(string sku, string partName)
    {
        var workbook = new PriceBookWorkbook
        {
            Rows = [new PriceRow
            {
                SheetName = "Indoor Price Book", Sku = sku, PartName = partName,
                Description = "Light Stone Traditional 46", Price = 483m
            }]
        };
        Assert.Null(FindFeature(workbook, "46", "Offset Light Stone Brick Traditional"));
    }

    [Theory]
    [InlineData("Analog Wall Timer", "AWALLTIMER", 89)]
    [InlineData("Active Heat Flex", "HFAK2570I", 2477)]
    public async Task TraditionalTimerAndActiveHeatFlexScaleWithFireplaceQuantity(
        string feature, string sku, int unitMsrp)
    {
        var request = Request("TRBON46", "46", feature);
        request.Fireplaces[0].Quantity = 2;
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(result.Success, result.Message);
        var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
        Assert.Equal(sku, line.Sku);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(unitMsrp * 2m, line.Price);
    }

    [Fact]
    public async Task MissingAnalogWallTimerSkuFailsPricingWithoutBorrowingAnotherTimer()
    {
        var path = Path.Join(Path.GetTempPath(), $"flare-missing-wall-timer-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook(PricingPath()))
            {
                var timer = Assert.Single(workbook.Worksheet("Parts Price Book").RowsUsed(),
                    row => row.Cell(3).GetString().Trim() == "AWALLTIMER");
                // Keep the matching description, but make this an unrelated timer part.
                timer.Cell(1).Value = "Other Wall Timer";
                timer.Cell(3).Value = "OTHERWALLTIMER";
                workbook.SaveAs(path);
            }
            var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
                Request("TRBON46", "46", "Analog Wall Timer"), path);

            Assert.False(result.Success);
            var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
            Assert.Null(line.Price);
            Assert.Empty(line.Sku);
            Assert.Contains("Analog Wall Timer", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Manufacturer Free Flow parts matrix: https://flarefireplaces.com/wp-content/uploads/Data/FreeFlow/FRFL-Parts.pdf
    [Theory]
    [InlineData("TR42", "42", "140", "70")]
    [InlineData("TR46", "46", "140", "70")]
    [InlineData("TRBON42", "42", "140", "70")]
    [InlineData("TRBON46", "46", "140", "70")]
    [InlineData("FFPASS", "30", "140", "70")]
    [InlineData("STPASS", "30", "140", "70")]
    [InlineData("FF", "25", "100", "50")]
    [InlineData("FF", "30", "100", "50")]
    [InlineData("FF", "45", "100", "50")]
    [InlineData("FF", "50", "100", "50")]
    [InlineData("FF", "60", "140", "70")]
    [InlineData("FF", "70", "140", "70")]
    [InlineData("FF", "80", "200", "100")]
    [InlineData("FF", "100", "200", "100")]
    public async Task FreeFlowLouversFollowTheManufacturerRegisterMatrix(
        string model, string size, string releaseSize, string intakeSize)
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
            Request(model, size, "Free Flow Heat Release Louver", "Free Flow Air Intake Louver"), PricingPath());

        Assert.True(result.Success, result.Message);
        var lines = Assert.Single(result.Fireplaces).OptionalFeatures;
        Assert.Equal(2, lines.Count);
        Assert.Equal("LUV" + releaseSize, lines[0].Sku);
        Assert.Equal("LUV" + intakeSize, lines[1].Sku);
        Assert.Equal(LouverMsrp(releaseSize), decimal.Round(lines[0].Price!.Value, 2));
        Assert.Equal(LouverMsrp(intakeSize), decimal.Round(lines[1].Price!.Value, 2));
        Assert.Equal("Free Flow Heat Release Louver", lines[0].Feature);
        Assert.Equal("Standardized Free Flow heat release mount and louver", lines[0].Description);
        Assert.Equal("Air Intake Louver", lines[1].Feature);
    }

    [Theory]
    [InlineData("Heat Release Louver")]
    [InlineData("Free Flow Louver")]
    [InlineData("Free Flow Heat Release Louver")]
    public async Task LegacyAndNewFreeFlowNamesPriceTheSameTraditionalRelease(string displayName)
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
            Request("TRBON46", "46", displayName), PricingPath());

        Assert.True(result.Success, result.Message);
        var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
        Assert.Equal("LUV140", line.Sku);
        Assert.Equal(466.67m, decimal.Round(line.Price!.Value, 2));
        Assert.Equal("Free Flow Heat Release Louver", line.Feature);
    }

    [Theory]
    [InlineData("TR42", "42")]
    [InlineData("TR46", "46")]
    [InlineData("TRBON42", "42")]
    [InlineData("TRBON46", "46")]
    public async Task PassiveHeatFlexKeepsItsComplete100SquareInchKitPrice(string model, string size)
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(
            Request(model, size, "Passive Heat Flex"), PricingPath());

        Assert.True(result.Success, result.Message);
        var line = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
        Assert.Equal("HFPK100", line.Sku);
        Assert.Equal(1040m, line.Price);
    }

    private static decimal LouverMsrp(string size) => size switch
    {
        "50" => 266.67m,
        "70" => 333.33m,
        "100" => 400m,
        "140" => 466.67m,
        "200" => 666.67m,
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };

    private static QuoteRequest Request(string model, string size, params string[] features) => new()
    {
        Fireplaces = [new FireplaceQuote
        {
            Model = model, Size = size, GlassHeight = model.Contains("PASS", StringComparison.Ordinal) ? "60" : size == "25" ? "30" : "16",
            Features = features.Select(feature => new FeatureSelection { DisplayName = feature }).ToList()
        }]
    };

    private static PriceRow? FindFeature(PriceBookWorkbook workbook, string size, string feature) =>
        (PriceRow?)typeof(ClosedXmlPriceBookService)
            .GetMethod("FindFeatureRow", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [workbook, FireplaceType.Traditional, "Traditional", size, "", feature]);

    private static string PricingPath() => Path.Join(AppContext.BaseDirectory, "LocalData", "pricing.xlsx");
}
