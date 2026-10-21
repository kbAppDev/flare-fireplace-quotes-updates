using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class FireplaceDiscountPricingTests
{
    private static string PricingPath() => Path.Join(AppContext.BaseDirectory, "LocalData", "pricing.xlsx");

    [Fact]
    public async Task DiscountAffectsOnlyExtendedFireplaceBaseAndPreservesAllMsrp()
    {
        var input = new FireplaceQuote
        {
            Type = FireplaceType.Indoor,
            Model = "FF",
            Size = "60",
            GlassHeight = "16",
            Quantity = 2,
            DiscountPercent = 12.5m,
            Features = [new FeatureSelection { Key = "double_glass", DisplayName = "Double Glass" }],
            PremiumMedia = [new MediaSelection { Key = "black_balls_4", DisplayName = "Uniform 4\" Matte Black Stone Balls", IsPremium = true }]
        };
        var request = new QuoteRequest { Fireplaces = [input], DiscountTerms = "Order by November 30.\nPayment through your dealer." };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(priced.Success, priced.Message);
        var fireplace = Assert.Single(priced.Fireplaces);
        Assert.Equal(22526m, fireplace.BaseLine.Price);
        Assert.Equal(22526m, fireplace.DiscountEligibleMsrp);
        Assert.Equal(2815.75m, fireplace.DiscountAmount);
        Assert.Equal(19710.25m, fireplace.DiscountedBasePrice);
        Assert.Equal(5290m, fireplace.OptionalFeatures.Single(row => row.Sku == "DGFF60R").Price);
        Assert.Equal(946m, fireplace.OptionalFeatures.Single(row => row.Sku == "PSBBU4-60").Price);
        Assert.Equal(28762m, priced.TotalMsrp);
        Assert.Equal(25946.25m, priced.TotalAfterDiscount);
        Assert.Equal(2815.75m, priced.TotalDiscount);
        Assert.Equal(12.5m, input.DiscountPercent);
        Assert.Equal(request.DiscountTerms, priced.Request.DiscountTerms);
    }

    [Theory]
    [InlineData(1, 1912.9, 21296.1)]
    [InlineData(3, 5738.7, 63888.3)]
    public async Task IncludedOutdoorKitRemainsFullPrice(int quantity, decimal discount, decimal newBase)
    {
        var request = new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote
            {
                Type = FireplaceType.IndoorOutdoorSeeThrough, Model = "ST60HOD", Size = "60", GlassHeight = "24",
                Quantity = quantity, DiscountPercent = 10m
            }]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(priced.Success, priced.Message);
        var fireplace = Assert.Single(priced.Fireplaces);
        Assert.Equal(23209m * quantity, fireplace.BaseLine.Price);
        Assert.Equal(19129m * quantity, fireplace.DiscountEligibleMsrp);
        Assert.Equal(discount, fireplace.DiscountAmount);
        Assert.Equal(newBase, fireplace.DiscountedBasePrice);
    }

    [Fact]
    public async Task DifferentFireplacesKeepIndependentPercentagesAndZeroDiscountDefaults()
    {
        var request = new QuoteRequest
        {
            Fireplaces =
            [
                new FireplaceQuote { Type = FireplaceType.Indoor, Model = "FF", Size = "60", GlassHeight = "16", DiscountPercent = 10m },
                new FireplaceQuote { Type = FireplaceType.IndoorSeeThrough, Model = "STPASS", Size = "30", GlassHeight = "60", DiscountPercent = 5m },
                new FireplaceQuote { Type = FireplaceType.Outdoor, Model = "VLC", Size = "60", GlassHeight = "24" }
            ]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(priced.Success, priced.Message);
        Assert.Equal(3, priced.Fireplaces.Count);
        Assert.Equal(1126.30m, priced.Fireplaces[0].DiscountAmount);
        Assert.Equal(973.30m, priced.Fireplaces[1].DiscountAmount);
        Assert.False(priced.Fireplaces[2].HasDiscount);
        Assert.Equal(0m, priced.Fireplaces[2].DiscountAmount);
        Assert.Equal(priced.Fireplaces[2].TotalMsrp, priced.Fireplaces[2].TotalAfterDiscount);
        Assert.Equal(45049m, priced.TotalMsrp);
        Assert.Equal(2099.60m, priced.TotalDiscount);
        Assert.Equal(42949.40m, priced.TotalAfterDiscount);
    }

    [Fact]
    public async Task OneHundredPercentStillChargesKitAndSelectedOptions()
    {
        var request = new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote
            {
                Type = FireplaceType.IndoorOutdoorSeeThrough, Model = "ST60HOD", Size = "60", GlassHeight = "24",
                DiscountPercent = 100m,
                Features = [new FeatureSelection { Key = "double_glass", DisplayName = "Double Glass" }]
            }]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(priced.Success, priced.Message);
        var fireplace = Assert.Single(priced.Fireplaces);
        Assert.Equal(19129m, fireplace.DiscountAmount);
        Assert.Equal(4080m, fireplace.DiscountedBasePrice);
        Assert.Equal(4469m, Assert.Single(fireplace.OptionalFeatures).Price);
        Assert.Equal(8549m, fireplace.TotalAfterDiscount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidPercentagesFailBeforeAnyQuoteCanBeUsed(decimal percent)
    {
        var request = new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote { Model = "FF", Size = "60", GlassHeight = "16", DiscountPercent = percent }]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.False(priced.Success);
        Assert.Empty(priced.Fireplaces);
        Assert.Contains("between 0% and 100%", priced.Message);
    }

    [Fact]
    public void DiscountRoundsToCentsAwayFromZeroWithoutChangingOriginalMsrp()
    {
        var fireplace = new PricedFireplaceQuote
        {
            BaseLine = new PriceLine { Price = 100.05m },
            DiscountEligibleMsrp = 100.05m,
            DiscountPercent = 10m,
            OptionalFeatures = [new PriceLine { Price = 20m }]
        };

        Assert.Equal(10.01m, fireplace.DiscountAmount);
        Assert.Equal(90.04m, fireplace.DiscountedBasePrice);
        Assert.Equal(120.05m, fireplace.TotalMsrp);
        Assert.Equal(110.04m, fireplace.TotalAfterDiscount);
    }

    [Fact]
    public async Task FractionalHeatFlexAndLouverPricesMatchDisplayedConfigurationTotal()
    {
        var request = new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote
            {
                Type = FireplaceType.Indoor, Model = "FF25EH", Size = "25", GlassHeight = "30", DiscountPercent = 10m,
                Features =
                [
                    new FeatureSelection { Key = "passive_heat_flex", DisplayName = "Passive Heat Flex" },
                    new FeatureSelection { Key = "air_intake_louver", DisplayName = "Air Intake Louver" }
                ]
            }]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.True(priced.Success, priced.Message);
        var fireplace = Assert.Single(priced.Fireplaces);
        Assert.Equal(6734m, fireplace.BaseLine.Price);
        Assert.Equal(673.40m, fireplace.DiscountAmount);
        Assert.Equal(6060.60m, fireplace.DiscountedBasePrice);
        Assert.Equal(866.67m, decimal.Round(fireplace.OptionalFeatures.Single(line => line.Sku == "HFPK70").Price!.Value, 2));
        Assert.Equal(266.67m, decimal.Round(fireplace.OptionalFeatures.Single(line => line.Sku == "LUV50").Price!.Value, 2));
        Assert.Equal(7193.94m, fireplace.TotalAfterDiscount);
    }

    [Fact]
    public async Task MissingBasePriceCannotProduceADiscountedQuote()
    {
        var request = new QuoteRequest
        {
            Fireplaces = [new FireplaceQuote { Type = FireplaceType.Indoor, Model = "Unknown", Size = "999", GlassHeight = "16", DiscountPercent = 10m }]
        };
        var priced = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath());

        Assert.False(priced.Success);
        Assert.False(Assert.Single(priced.Fireplaces).HasDiscount);
        Assert.Null(priced.Fireplaces[0].DiscountedBasePrice);
    }
}
