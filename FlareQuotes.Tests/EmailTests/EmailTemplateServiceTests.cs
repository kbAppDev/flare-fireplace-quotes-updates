using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.EmailTests;

public sealed class EmailTemplateServiceTests
{
    private const string IndoorFirstParagraph =
        "Below are links to the product information, including a quote for the fireplace and its optional features. " +
        "The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 days, and do not include installation costs.";

    private const string LegacySingularFirstParagraph =
        "Below are links to the product information with a quote for the fireplace and its optional features. " +
        "The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 days, and do not include installation costs.";

    private const string LegacyPluralFirstParagraph =
        "Below are links to the product information with a quote for the fireplace(s) and their optional features. " +
        "The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 days, and do not include installation costs.";

    [Theory]
    [InlineData(FireplaceType.Indoor)]
    [InlineData(FireplaceType.IndoorSeeThrough)]
    [InlineData(FireplaceType.Traditional)]
    [InlineData(FireplaceType.Large)]
    public void IndoorFamilyQuotesUseExactRequestedOpening(FireplaceType type)
    {
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = [new PricedFireplaceQuote { Type = type }]
        });

        const string secondParagraph =
            "If you have any questions or are ready to proceed, please use the information in my email signature to reach me directly and schedule a " +
            "<a href=\"https://meetings.hubspot.com/kyle533/jobsite-consultation\">project consultation</a>.";
        Assert.StartsWith("<strong><em>Taylor,</em></strong><br>" + IndoorFirstParagraph + "<br><br><br>" +
                              secondParagraph + "<br><br><br>", html);
        Assert.DoesNotContain(LegacySingularFirstParagraph, html);
    }

    [Fact]
    public void MultipleIndoorFireplacesKeepExactRequestedSingularOpening()
    {
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces =
            [
                new PricedFireplaceQuote { Type = FireplaceType.Indoor, Quantity = 3 },
                new PricedFireplaceQuote { Type = FireplaceType.IndoorSeeThrough }
            ]
        });

        Assert.Contains(IndoorFirstParagraph, html);
        Assert.DoesNotContain(LegacyPluralFirstParagraph, html);
    }

    [Theory]
    [InlineData(FireplaceType.Outdoor, 1)]
    [InlineData(FireplaceType.Outdoor, 3)]
    [InlineData(FireplaceType.OutdoorSeeThrough, 1)]
    [InlineData(FireplaceType.OutdoorSeeThrough, 3)]
    [InlineData(FireplaceType.IndoorOutdoorSeeThrough, 1)]
    [InlineData(FireplaceType.IndoorOutdoorSeeThrough, 3)]
    public void OutdoorAndHybridQuotesKeepExistingOpening(FireplaceType type, int quantity)
    {
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = [new PricedFireplaceQuote { Type = type, Quantity = quantity }]
        });

        Assert.Contains(quantity > 1 ? LegacyPluralFirstParagraph : LegacySingularFirstParagraph, html);
        Assert.DoesNotContain(IndoorFirstParagraph, html);
    }

    [Theory]
    [InlineData(FireplaceType.Outdoor, false)]
    [InlineData(FireplaceType.Outdoor, true)]
    [InlineData(FireplaceType.OutdoorSeeThrough, false)]
    [InlineData(FireplaceType.OutdoorSeeThrough, true)]
    [InlineData(FireplaceType.IndoorOutdoorSeeThrough, false)]
    [InlineData(FireplaceType.IndoorOutdoorSeeThrough, true)]
    public void MixedQuotesKeepExistingOpeningRegardlessOfFireplaceOrder(FireplaceType otherType, bool indoorFirst)
    {
        var indoor = new PricedFireplaceQuote { Type = FireplaceType.Indoor };
        var other = new PricedFireplaceQuote { Type = otherType };
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = indoorFirst ? [indoor, other] : [other, indoor]
        });

        Assert.Contains(LegacyPluralFirstParagraph, html);
        Assert.DoesNotContain(IndoorFirstParagraph, html);
    }

    [Theory]
    [InlineData(FireplaceType.Indoor, true)]
    [InlineData(FireplaceType.IndoorSeeThrough, true)]
    [InlineData(FireplaceType.Traditional, true)]
    [InlineData(FireplaceType.Large, true)]
    [InlineData(FireplaceType.Outdoor, false)]
    [InlineData(FireplaceType.OutdoorSeeThrough, false)]
    [InlineData(FireplaceType.IndoorOutdoorSeeThrough, false)]
    [InlineData(FireplaceType.Unknown, false)]
    public void QuotesWithoutPricedFireplacesUseRequestTypes(FireplaceType type, bool expectedIndoorCopy)
    {
        var request = new QuoteRequest
        {
            ClientName = "Taylor",
            Fireplaces = [new FireplaceQuote { Type = type }]
        };
        var html = BuildHtml(new PricedQuoteResult(), request);

        Assert.Contains(expectedIndoorCopy ? IndoorFirstParagraph : LegacySingularFirstParagraph, html);
    }

    [Fact]
    public void QuotesWithoutAnyFireplaceTypeKeepExistingOpening()
    {
        var html = BuildHtml(new PricedQuoteResult());

        Assert.Contains(LegacySingularFirstParagraph, html);
        Assert.DoesNotContain(IndoorFirstParagraph, html);
    }

    [Fact]
    public void PricedTypesTakePrecedenceOverUnpricedRequestTypes()
    {
        var request = new QuoteRequest
        {
            ClientName = "Taylor",
            Fireplaces = [new FireplaceQuote { Type = FireplaceType.Outdoor }]
        };
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = [new PricedFireplaceQuote { Type = FireplaceType.Indoor }]
        }, request);

        Assert.Contains(IndoorFirstParagraph, html);
    }

    [Fact]
    public void OutdoorOpeningPreservesConfiguredConsultationLink()
    {
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = [new PricedFireplaceQuote { Type = FireplaceType.Outdoor }]
        }, settings: new AppSettings { ConsultationUrl = "https://meetings.hubspot.com/kyle533/custom-consultation" });

        Assert.Contains("<a href=\"https://meetings.hubspot.com/kyle533/custom-consultation\">project consultation</a>.", html);
    }

    [Theory]
    [InlineData("https://flarefireplaces.com")]
    [InlineData("https://meetings.hubspot.com/kyle533/custom-consultation")]
    public void IndoorQuotesUseRequestedConsultationLinkRegardlessOfSettings(string configuredUrl)
    {
        var html = BuildHtml(new PricedQuoteResult
        {
            Fireplaces = [new PricedFireplaceQuote { Type = FireplaceType.Indoor }]
        }, settings: new AppSettings { ConsultationUrl = configuredUrl });

        Assert.Contains("<a href=\"https://meetings.hubspot.com/kyle533/jobsite-consultation\">project consultation</a>.", html);
        Assert.DoesNotContain(configuredUrl, html);
    }

    [Fact]
    public void SpecLinkHeadingPrefixesModelWithEncodedFireplaceLocation()
    {
        var html = BuildHtml(
            new ResourceLinkSet
            {
                FireplaceLocation = "Living & Dining",
                ModelNumber = "DVFF60H",
                Links = { ["Product Sheet"] = "https://flarefireplaces.com/product" }
            });

        Assert.Contains("<strong>Living &amp; Dining — DVFF60H Spec Files:</strong>", html);
    }

    [Fact]
    public void SpecLinkHeadingKeepsExistingModelOnlyTextWhenLocationIsBlank()
    {
        var html = BuildHtml(
            new ResourceLinkSet
            {
                FireplaceLocation = "  ",
                ModelNumber = "DVFF60H",
                Links = { ["Product Sheet"] = "https://flarefireplaces.com/product" }
            });

        Assert.Contains("<strong>DVFF60H Spec Files:</strong>", html);
        Assert.DoesNotContain("— DVFF60H Spec Files", html);
    }

    [Fact]
    public void UnapprovedResourceLinksAreNotIncludedInCustomerEmail()
    {
        var html = BuildHtml(
            new ResourceLinkSet
            {
                ModelNumber = "DVFF60H",
                Links = { ["Untrusted"] = "https://example.com/redirect" }
            });

        Assert.DoesNotContain("example.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No verified resource links are available", html, StringComparison.Ordinal);
    }

    private static string BuildHtml(ResourceLinkSet resourceLinkSet)
    {
        return new EmailTemplateService().BuildHtml(
            new QuoteRequest { ClientName = "Taylor" },
            new PricedQuoteResult(),
            [resourceLinkSet],
            new AppSettings(),
            string.Empty);
    }

    private static string BuildHtml(PricedQuoteResult priced, QuoteRequest? request = null, AppSettings? settings = null)
    {
        return new EmailTemplateService().BuildHtml(
            request ?? new QuoteRequest { ClientName = "Taylor" },
            priced,
            [],
            settings ?? new AppSettings(),
            string.Empty);
    }
}
