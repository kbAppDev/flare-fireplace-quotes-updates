using System.Reflection;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class QuotePdfFileNameTests
{
    [Fact]
    public void AppendsActualCustomerNameToSingleFireplaceFileName()
    {
        var fileName = BuildFileName(
            new QuoteRequest { ClientName = "Amanda Jensen" },
            PricedQuote("FF60R"));

        Assert.Equal("Flare Fireplace Quote - FF60R - Amanda Jensen.pdf", fileName);
    }

    [Fact]
    public void BlankCustomerNameDoesNotAddDanglingSuffix()
    {
        var fileName = BuildFileName(
            new QuoteRequest { ClientName = "   " },
            PricedQuote("FF60R"));

        Assert.Equal("Flare Fireplace Quote - FF60R.pdf", fileName);
    }

    [Fact]
    public void SanitizesCustomerNameBeforeAppendingIt()
    {
        var fileName = BuildFileName(
            new QuoteRequest { ClientName = "  Jamie: Rivera / VIP?  " },
            PricedQuote("FF60R"));

        Assert.Equal("Flare Fireplace Quote - FF60R - Jamie Rivera VIP.pdf", fileName);
    }

    [Fact]
    public void AppendsCustomerNameToMultiFireplaceFileName()
    {
        var fileName = BuildFileName(
            new QuoteRequest { ClientName = "Taylor Morgan" },
            PricedQuote("FF60R", "ST70R"));

        Assert.Equal("Flare Fireplaces Quote - FF60R and ST70R - Taylor Morgan.pdf", fileName);
    }

    private static string BuildFileName(QuoteRequest request, PricedQuoteResult priced)
    {
        var method = typeof(MainViewModel).GetMethod(
            "BuildQuotePdfFileName",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, new object?[] { request, priced }));
    }

    private static PricedQuoteResult PricedQuote(params string[] modelNumbers)
    {
        return new PricedQuoteResult
        {
            Fireplaces = modelNumbers.Select(modelNumber => new PricedFireplaceQuote
            {
                ModelNumber = modelNumber
            }).ToList()
        };
    }
}
