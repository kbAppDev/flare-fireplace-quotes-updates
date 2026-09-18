using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.EmailTests;

public sealed class EmailTemplateServiceTests
{
    [Fact]
    public void SpecLinkHeadingPrefixesModelWithEncodedFireplaceLocation()
    {
        var html = BuildHtml(
            new ResourceLinkSet
            {
                FireplaceLocation = "Living & Dining",
                ModelNumber = "DVFF60H",
                Links = { ["Product Sheet"] = "https://example.com/product" }
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
                Links = { ["Product Sheet"] = "https://example.com/product" }
            });

        Assert.Contains("<strong>DVFF60H Spec Files:</strong>", html);
        Assert.DoesNotContain("— DVFF60H Spec Files", html);
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
}
