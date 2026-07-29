using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.ParserTests;

public sealed class QuoteProjectTitleFormatterTests
{
    [Fact]
    public void FormatsOutdoorOpeningWithoutCallingItGlass()
    {
        var fireplace = new PricedFireplaceQuote
        {
            Type = FireplaceType.OutdoorSeeThrough,
            Model = "Outdoor Vent Free See Through",
            Size = "50",
            GlassHeight = "16"
        };

        Assert.Equal("Outdoor See Through 50\" x 16\"",
                     QuoteProjectTitleFormatter.BuildFireplaceTitle(fireplace));
    }

    [Fact]
    public void FormatsIndoorFireplaceWithoutCallingItGlass()
    {
        var fireplace = new PricedFireplaceQuote
        {
            Type = FireplaceType.Indoor,
            Model = "Front Facing",
            Size = "80",
            GlassHeight = "24"
        };

        Assert.Equal("Indoor Front Facing 80\" x 24\"",
                     QuoteProjectTitleFormatter.BuildFireplaceTitle(fireplace));
    }

    [Fact]
    public void ListsEveryFireplaceInQuoteOrder()
    {
        var fireplaces = new[]
        {
            new PricedFireplaceQuote
            {
                Type = FireplaceType.OutdoorSeeThrough,
                Model = "See Through",
                Size = "50",
                GlassHeight = "16"
            },
            new PricedFireplaceQuote
            {
                Type = FireplaceType.Indoor,
                Model = "Front Facing",
                Size = "80",
                GlassHeight = "24"
            }
        };

        Assert.Equal("Outdoor See Through 50\" x 16\" | Indoor Front Facing 80\" x 24\"",
                     QuoteProjectTitleFormatter.BuildFallback(fireplaces));
    }
}
