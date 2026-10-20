using System.Reflection;
using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using FlareQuotes.Tests.TestSupport;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class PricingMappingAuditTests
{
    private static string PricingPath() => Path.Combine(AppContext.BaseDirectory, "LocalData", "pricing.xlsx");
    private static PriceRow? Feature(PriceBookWorkbook wb, FireplaceType type, string model, string size, string height, string feature) =>
        (PriceRow?)typeof(ClosedXmlPriceBookService).GetMethod("FindFeatureRow", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [wb, type, model, size, height, feature]);

    [Fact]
    public void DuraFlexDuplicateSkuUsesConfirmedPriceInEverySheet()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(PricingPath());
        var matches = workbook.Worksheets.SelectMany(sheet => sheet.RowsUsed())
            .Where(row => row.Cell(3).GetString().Trim() == "4DFA25").ToList();
        Assert.Equal(2, matches.Count);
        Assert.All(matches, row => Assert.Equal(335m, row.Cell(4).GetValue<decimal>()));
    }

    [Theory]
    [InlineData(FireplaceType.Indoor, "LC", "RBLC60H")]
    [InlineData(FireplaceType.Outdoor, "VLC", "VFRBLC60H")]
    public async Task LeftCorner60HighReflectiveBackUsesConfirmedMsrp(FireplaceType type, string model, string sku)
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var row = Feature(wb, type, model, "60", "24", "Reflective Black Back");
        Assert.NotNull(row);
        Assert.Equal(sku, row.Sku);
        Assert.Equal(888m, row.Price);
    }

    [Theory]
    [InlineData("50", "16", 404, 501)]
    [InlineData("60", "16", 444, 444)]
    [InlineData("70", "16", 467, 467)]
    [InlineData("80", "16", 474, 474)]
    [InlineData("100", "16", 626, 626)]
    [InlineData("50", "24", 510, 640)]
    [InlineData("60", "24", 564, 694)]
    [InlineData("70", "24", 592, 722)]
    [InlineData("80", "24", 600, 730)]
    [InlineData("100", "24", 864, 994)]
    public async Task OutdoorCornerScreensUse2026SkuAndModelPrice(
        string size, string height, int cornerMsrp, int doubleCornerMsrp)
    {
        var service = new ClosedXmlPriceBookService();
        var wb = await service.LoadAsync(PricingPath());
        var suffix = height == "24" ? "H" : string.Empty;
        foreach (var style in new[] { "LC", "RC", "DC" })
        {
            var expectedSku = $"VSC{(style == "DC" ? "DC" : "CNR")}{size}{suffix}";
            var expectedPart = $"VF-SCREEN-{style}-{size}{(height == "24" ? "-H" : string.Empty)}";
            var expectedMsrp = (decimal)(style == "DC" ? doubleCornerMsrp : cornerMsrp);
            for (int order = 0; order < 2; order++)
            {
                var row = Feature(wb, FireplaceType.Outdoor, "V" + style, size, height, "Safety Screen");
                Assert.NotNull(row);
                Assert.Equal(expectedPart, row.PartName, ignoreCase: true);
                Assert.Equal(expectedSku, row.Sku);
                Assert.Equal(expectedMsrp, row.Price);
                wb.Rows.Reverse();
            }

            var result = await service.BuildPricedQuoteAsync(new QuoteRequest
            {
                Fireplaces = [new FireplaceQuote {
                    Type = FireplaceType.Outdoor, Model = "V" + style, Size = size, GlassHeight = height,
                    Features = [new FeatureSelection { Key = "safety_screen", DisplayName = "Safety Screen" }]
                }]
            }, PricingPath());
            Assert.True(result.Success, result.Message);
            var screen = Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures);
            Assert.Equal(expectedSku, screen.Sku);
            Assert.Equal(expectedMsrp, screen.Price);
        }
    }

    [Fact]
    public async Task FrontFacing25ExtraHighUsesCorrected2026BaseSku()
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote {
                Type = FireplaceType.Indoor, Model = "FF25EH", Size = "25", GlassHeight = "30"
            }]
        }, PricingPath());
        Assert.True(result.Success, result.Message);
        var fireplace = Assert.Single(result.Fireplaces);
        Assert.Equal("DVFF25E", fireplace.BaseLine.Sku);
        Assert.Equal(6734m, fireplace.BaseLine.Price);
        Assert.Equal(6734m, result.TotalMsrp);
    }

    [Theory]
    [InlineData("FFPASS", "Double Glass", "DGPAFF")]
    [InlineData("STPASS", "Double Glass", "DGPAST")]
    [InlineData("PASSST", "Double Glass", "DGPAST")]
    [InlineData("STPASSOD", "Double Glass", "DGPAST")]
    [InlineData("STPASSIO", "Double Glass", "DGPAST")]
    [InlineData("FFPASS", "Reflective Black Sides", "RBSPAFF")]
    [InlineData("STPASS", "Reflective Black Sides", "RBSPAST")]
    [InlineData("FFPASS", "Reflective Black Back", "RBPAFF")]
    [InlineData("FFPASS", "RGB LEDs", "RGBPA")]
    [InlineData("STPASS", "RGB LEDs", "RGBPA")]
    [InlineData("STPASS", "Summit Burner", "SUMPASS")]
    public async Task PassageOptionsUseTheirOwnModelOrSharedRow(string model, string feature, string expectedSku)
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var row = Feature(wb, model.Contains("ST") ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor,
            model, "30", "60", feature);
        Assert.NotNull(row);
        Assert.Equal(expectedSku, row.Sku);
        Assert.Equal(wb.Rows.Single(r => r.Sku == expectedSku).Price, row.Price);
        wb.Rows.Reverse();
        Assert.Equal(expectedSku, Feature(wb, FireplaceType.Indoor, model, "30", "60", feature)?.Sku);
    }

    [Theory]
    [InlineData("STPASS", "Double Glass", "DGPAST")]
    [InlineData("FFPASS", "Double Glass", "DGPAFF")]
    [InlineData("STPASS", "Reflective Black Sides", "RBSPAST")]
    public async Task MissingPassageOptionNeverBorrowsAnotherModel(string model, string feature, string removedSku)
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        wb.Rows.RemoveAll(r => r.Sku == removedSku);
        Assert.Null(Feature(wb, FireplaceType.Indoor, model, "30", "60", feature));
    }

    [Fact]
    public async Task SeeThroughPassageNeverGetsReflectiveBack()
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        Assert.Null(Feature(wb, FireplaceType.IndoorSeeThrough, "STPASS", "30", "60", "Reflective Black Back"));
    }

    [Fact]
    public async Task EveryCatalogBaseMatchesWorkbookSkuAndPrice()
    {
        var service = new ClosedXmlPriceBookService();
        var wb = await service.LoadAsync(PricingPath());
        var failures = new List<string>();
        var models = PriceBookModelCatalog.GetFireplaceModels(wb);
        foreach (var expected in models)
        {
            var model = expected.PartName.Replace("FLARE-", "", StringComparison.OrdinalIgnoreCase);
            if (expected.Sku == "DVPAFF") model = "FFPASS";
            if (expected.Sku == "DVPAST") model = "STPASS";
            var size = Regex.Match(model, @"\d+").Value;
            var height = expected.Description.Contains("x 30", StringComparison.OrdinalIgnoreCase) ||
                model.EndsWith("EH", StringComparison.OrdinalIgnoreCase) ? "30" :
                model.EndsWith("H", StringComparison.OrdinalIgnoreCase) ? "24" : "16";
            var type = PriceBookModelCatalog.Type(expected.Sku);
            var result = await service.BuildPricedQuoteAsync(new QuoteRequest
            {
                Fireplaces = [new FireplaceQuote { Type = type, Model = model, Size = size, GlassHeight = height }]
            }, PricingPath());
            var actual = result.Fireplaces.SingleOrDefault()?.BaseLine;
            if (actual?.Sku != expected.Sku || actual.Price != expected.Price)
                failures.Add(expected.Sku + ": got " + actual?.Sku + " " + actual?.Price + "; expected " + expected.Price);
        }
        Assert.Equal(268, models.Count);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task EveryStandardIndoorDoubleGlassRowMatchesItsStyleSizeAndHeight()
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var failures = new List<string>();
        int count = 0;
        foreach (var expected in wb.Rows.Where(r => r.SheetName == "Indoor Price Book" &&
            Regex.IsMatch(r.Sku, @"^DG(FF|ST|LC|RC|DC|RD)\d+(R|H|E|EH)$")))
        {
            var match = Regex.Match(expected.Sku, @"^DG(FF|ST|LC|RC|DC|RD)(\d+)(R|H|E|EH)$");
            var style = match.Groups[1].Value;
            var size = match.Groups[2].Value;
            var height = match.Groups[3].Value == "R" ? "16" : match.Groups[3].Value == "H" ? "24" : "30";
            var actual = Feature(wb, style == "ST" ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor,
                style, size, height, "Double Glass");
            count++;
            if (actual?.Sku != expected.Sku || actual.Price != expected.Price)
                failures.Add(expected.Sku + ": got " + actual?.Sku + " " + actual?.Price);
        }
        Assert.True(count > 80, "Expected the full indoor Double Glass range.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task WorkbookAccessoryRowsMatchModelSpecificLookups()
    {
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var failures = new List<string>();
        int checks = 0;
        foreach (var expected in wb.Rows.ToList())
        {
            string? feature = null;
            string model = "FF", size = "60", height = "16";
            var type = expected.SheetName == "Outdoor Price Book" ? FireplaceType.Outdoor :
                expected.SheetName == "Large Price Book" ? FireplaceType.Large : FireplaceType.Indoor;
            var part = expected.PartName.ToUpperInvariant();
            var m = Regex.Match(part, @"^(?:VF-)?(RB|RBS|SCREEN)-(FF|ST|LC|RC|DC|RD)(?:-(\d+))?(?:-(R|H|EH))?$");
            if (m.Success)
            {
                feature = m.Groups[1].Value == "RB" ? "Reflective Black Back" :
                    m.Groups[1].Value == "RBS" ? "Reflective Black Sides" : "Safety Screen";
                model = m.Groups[2].Value;
                if (m.Groups[3].Success) size = m.Groups[3].Value;
                height = m.Groups[4].Value == "H" ? "24" : m.Groups[4].Value == "EH" ? "30" : "16";
                if (model == "ST") type = type == FireplaceType.Outdoor ? FireplaceType.OutdoorSeeThrough : FireplaceType.IndoorSeeThrough;
            }
            m = Regex.Match(part, @"^OD-ST-(\d+)(?:-(R|H|EH))?$");
            if (m.Success)
            {
                feature = "Outdoor Kit"; model = "ST"; size = m.Groups[1].Value;
                height = m.Groups[2].Value == "H" ? "24" : m.Groups[2].Value == "EH" ? "30" : "16";
                type = FireplaceType.IndoorSeeThrough;
            }
            m = Regex.Match(part, @"^(?:LED-RGB|SUM-FLARE)-(\d+)$");
            if (m.Success)
            {
                feature = part.StartsWith("LED") ? "RGB LEDs" : "Summit Burner";
                size = m.Groups[1].Value;
            }
            m = Regex.Match(part, @"^(CLG|RB|RD|NT|BL|HB-RD|HB-NT|HB-BL|HB-LS)-TRA-(42|46)$");
            if (m.Success)
            {
                model = "TRA"; size = m.Groups[2].Value; type = FireplaceType.Traditional;
                feature = m.Groups[1].Value switch
                {
                    "CLG" => "Double Glass",
                    "RB" => "Reflective Black Interior",
                    "RD" => "Offset Red Brick Traditional",
                    "NT" => "Offset Natural Brick Traditional",
                    "BL" => "Offset Black Brick Traditional",
                    "HB-RD" => "Herringbone Red Brick Traditional",
                    "HB-NT" => "Herringbone Natural Brick Traditional",
                    "HB-BL" => "Herringbone Black Brick Traditional",
                    _ => "Herringbone Light Stone Brick Traditional"
                };
            }
            if (feature is null) continue;
            if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough) model = "V" + model;
            checks++;
            var actual = Feature(wb, type, model, size, height, feature);
            if (actual?.Sku != expected.Sku || actual.Price != expected.Price)
                failures.Add(expected.SheetName + " " + expected.PartName + " (" + expected.Sku + "): got " + actual?.Sku +
                    " $" + actual?.Price + "; expected $" + expected.Price);
        }
        Assert.True(checks > 200, "Expected broad accessory coverage.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("STPASS", 1, 5294, 24760)]
    [InlineData("STPASS", 2, 10588, 49520)]
    [InlineData("FFPASS", 1, 3782, 19975)]
    public async Task PassageQuotedTotalsUseCorrectDoubleGlass(string model, int quantity, int glassTotal, int quoteTotal)
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote {
                Type = model == "STPASS" ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor,
                Model = model, Size = "30", GlassHeight = "60", Quantity = quantity,
                Features = [new FeatureSelection { Key = "double_glass", DisplayName = "Double Glass Safety Barrier" }]
            }]
        }, PricingPath());
        Assert.True(result.Success, result.Message);
        Assert.Equal(glassTotal, Assert.Single(Assert.Single(result.Fireplaces).OptionalFeatures).Price);
        Assert.Equal(quoteTotal, result.TotalMsrp);
    }

    [Fact]
    public async Task MediaVariantsKeepTheirOwnSkuWhenRowsAreReordered()
    {
        var expected = new Dictionary<string, string>
        {
            ["fg_silver"] = "CMDFGS",
            ["fg_bronze"] = "CMDFGBR",
            ["fg_black"] = "CMDFGBL",
            ["fg_ice"] = "CMDFGIC",
            ["fg_clear"] = "CMDFGCL",
            ["pd_black"] = "CMDPDB",
            ["pd_rain"] = "CMDPDR",
            ["cp_white"] = "CMDCPW",
            ["cp_black"] = "CMDCPB",
            ["cp_earth"] = "CMDCPE",
            ["gl_white"] = "CMDGLW",
            ["gl_wood"] = "CMDGLWO",
            ["gl_branches"] = "CMDGLB",
            ["gl_embers_tan"] = "CMDGLE",
            ["gl_embers_black"] = "CMDGLEB",
            ["gold_glass"] = "PMDFGG",
            ["aqua_glass"] = "PMDFGA",
            ["chestnut_glass"] = "PMDFGC",
            ["black_stones"] = "PMDPSB",
            ["white_stones"] = "PMDPSW",
            ["grey_balls_2"] = "PSBGU2",
            ["grey_balls_4"] = "PSBGU4",
            ["grey_balls_mixed"] = "PSBGM",
            ["white_balls_2"] = "PSBWU2",
            ["white_balls_4"] = "PSBWU4",
            ["white_balls_mixed"] = "PSBWM",
            ["black_balls_2"] = "PSBBU2",
            ["black_balls_4"] = "PSBBU4",
            ["black_balls_mixed"] = "PSBBM",
            ["birchwood"] = "PMDBIRCH",
            ["tr42bch"] = "TR42BCH",
            ["tr46bch"] = "TR46BCH",
            ["loak42"] = "LOAK42",
            ["loak46"] = "LOAK46"
        };
        var wb = await new ClosedXmlPriceBookService().LoadAsync(PricingPath());
        var failures = new List<string>();
        for (int order = 0; order < 2; order++)
        {
            foreach (var media in FlareQuotes.Core.Media.MediaCatalog.Classic.Concat(FlareQuotes.Core.Media.MediaCatalog.Premium))
            {
                if (!expected.TryGetValue(media.Key, out var sku)) continue; // Driftwood uses its own small/large quantity path.
                var actual = (PriceRow?)typeof(ClosedXmlPriceBookService).GetMethod("FindPremiumMediaRow",
                    BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [wb, media.Key, media.DisplayName]);
                if (actual?.Sku != sku)
                    failures.Add(media.Key + " order=" + order + ": expected " + sku + ", got " + actual?.Sku);
            }
            wb.Rows.Reverse();
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(1, 473)]
    [InlineData(2, 946)]
    public async Task BlackUniformFourInchStoneBallsKeepSizeSpecificQuotePricing(int quantity, int mediaMsrp)
    {
        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote {
                Type = FireplaceType.Indoor, Model = "FF", Size = "60", GlassHeight = "24", Quantity = quantity,
                PremiumMedia = [new MediaSelection {
                    Key = "black_balls_4", DisplayName = "Matte Black 4\" Stone Balls", IsPremium = true
                }]
            }]
        }, PricingPath());
        Assert.True(result.Success, result.Message);
        var fireplace = Assert.Single(result.Fireplaces);
        var media = Assert.Single(fireplace.OptionalFeatures);
        Assert.Equal("PSBBU4-60", media.Sku);
        Assert.Equal(quantity, media.Quantity);
        Assert.Equal((decimal)mediaMsrp, media.Price);
        Assert.Equal(fireplace.BaseLine.Price + mediaMsrp, result.TotalMsrp);
    }
}
