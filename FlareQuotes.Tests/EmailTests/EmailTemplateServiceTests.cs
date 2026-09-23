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
}
