using System.Text.RegularExpressions;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace FlareQuotes.Tests.PdfTests;

public sealed class DiscountQuoteRenderingTests
{
    [Fact]
    public async Task PdfPreservesMsrpAndShowsDiscountAndNewCostInRedWithCents()
    {
        var fireplace = Fireplace("DVFF60H", 20_502m, 20_002m, 12.5m);
        fireplace.Quantity = fireplace.BaseLine.Quantity = 2;
        fireplace.OptionalFeatures =
        [
            new PriceLine { Feature = "Power Vent", Description = "Power Vent", Price = 333.335m },
            new PriceLine { Feature = "Gold Glass", Description = "Gold Glass", Price = 44.445m }
        ];
        var request = Request(fireplace);
        request.DiscountTerms = "Payment received by October 31.\r\nSubject to product availability.";
        var path = TemporaryPdfPath();
        try
        {
            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, path,
                                                                  TestContext.Current.CancellationToken);
            using var pdf = PdfDocument.Open(path);
            Assert.Equal(1, pdf.NumberOfPages);
            var page = pdf.GetPage(1);
            var text = PageText(page);
            Assert.Contains("Total MSRP", text);
            Assert.Contains("$20,502", text);
            Assert.Contains("$333.34", text);
            Assert.Contains("$44.45", text);
            Assert.Contains("Fireplace discount (12.5%): -$2,500.25 | New fireplace cost: $18,001.75", text);
            Assert.Contains("Configuration total after fireplace discount (including selected options): $18,379.54", text);
            Assert.Contains("Discount Terms", text);
            Assert.Contains("Payment received by October 31.", text);
            Assert.Contains("Subject to product availability.", text);
            Assert.Contains("any included outdoor kit remain at MSRP", text);

            var redText = Compact(string.Concat(page.Letters.Where(IsFlareRed).Select(letter => letter.Value)));
            Assert.Contains(Compact("Fireplace discount (12.5%): -$2,500.25"), redText);
            Assert.Contains(Compact("New fireplace cost: $18,001.75"), redText);
            Assert.Contains(Compact("$18,379.54"), redText);
            Assert.DoesNotContain(Compact("$20,502"), redText);
            Assert.DoesNotContain(Compact("$333.34"), redText);
            Assert.Equal(20_502m, fireplace.BaseLine.Price);
            Assert.Equal(333.335m, fireplace.OptionalFeatures[0].Price);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PdfKeepsDifferentDiscountsAndTermsOnTheirOwnFireplacePages()
    {
        var request = Request(Fireplace("FIRST-MODEL", 8_000m, 8_000m, 10m),
                              Fireplace("SECOND-MODEL", 10_000m, 10_000m, 20m),
                              Fireplace("FULL-PRICE-MODEL", 6_000m, 6_000m, 0m));
        request.DiscountTerms = "Discount valid through October 31.";
        var path = TemporaryPdfPath();
        try
        {
            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, path,
                                                                  TestContext.Current.CancellationToken);
            using var pdf = PdfDocument.Open(path);
            Assert.Equal(3, pdf.NumberOfPages);
            var first = PageText(pdf.GetPage(1));
            var second = PageText(pdf.GetPage(2));
            var third = PageText(pdf.GetPage(3));
            Assert.Contains("Fireplace discount (10%): -$800 | New fireplace cost: $7,200", first);
            Assert.DoesNotContain("SECOND-MODEL", first);
            Assert.Contains("Fireplace discount (20%): -$2,000 | New fireplace cost: $8,000", second);
            Assert.DoesNotContain("FIRST-MODEL", second);
            Assert.Contains(request.DiscountTerms, first);
            Assert.Contains(request.DiscountTerms, second);
            Assert.DoesNotContain("Discount Terms", third);
            Assert.DoesNotContain("Fireplace discount", third);
            Assert.DoesNotContain(request.DiscountTerms, third);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LongMultilineDiscountTermsWrapAcrossPagesWithoutClippingOrTruncation()
    {
        var request = Request(Fireplace("TERMS-MODEL", 8_000m, 8_000m, 10m));
        request.DiscountTerms = string.Join("\n", Enumerable.Range(1, 90).Select(index =>
            $"Term {index:D3}: Confirm the delivery address and payment schedule with your sales representative before placing the fireplace order.")) +
            "\nFINAL DISCOUNT TERM.";
        var path = TemporaryPdfPath();
        try
        {
            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, path,
                                                                  TestContext.Current.CancellationToken);
            using var pdf = PdfDocument.Open(path);
            Assert.True(pdf.NumberOfPages > 1, "Long terms should continue onto additional pages.");
            var pages = pdf.GetPages().ToList();
            var text = string.Join(" ", pages.Select(PageText));
            for (var index = 1; index <= 90; index++)
                Assert.Contains($"Term {index:D3}:", text);
            Assert.Contains("FINAL DISCOUNT TERM.", PageText(pages[^1]));
            Assert.All(pages.SelectMany(page => page.Letters).Where(letter => !string.IsNullOrWhiteSpace(letter.Value)), letter =>
            {
                Assert.InRange(letter.BoundingBox.Left, 72d, 540d);
                Assert.InRange(letter.BoundingBox.Right, 72d, 540d);
                Assert.InRange(letter.BoundingBox.Bottom, 50d, 736d);
                Assert.InRange(letter.BoundingBox.Top, 50d, 736d);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PdfWithoutDiscountKeepsExistingMsrpCopyAndOmitsDiscountTerms()
    {
        var fireplace = Fireplace("FULL-PRICE-MODEL", 8_000m, 8_000m, 0m);
        fireplace.OptionalFeatures = [new PriceLine { Feature = "Media", Description = "Media", Price = 333.33m }];
        var request = Request(fireplace);
        request.DiscountTerms = "Unused discount terms.";
        var path = TemporaryPdfPath();
        try
        {
            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, path,
                                                                  TestContext.Current.CancellationToken);
            using var pdf = PdfDocument.Open(path);
            Assert.Equal(1, pdf.NumberOfPages);
            var page = pdf.GetPage(1);
            var text = PageText(page);
            Assert.Contains("The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 days, and do not include venting or installation costs.", text);
            Assert.Contains("$8,000", text);
            Assert.Contains("$333", text);
            Assert.DoesNotContain("$333.33", text);
            Assert.DoesNotContain("Fireplace discount", text);
            Assert.DoesNotContain("New fireplace cost", text);
            Assert.DoesNotContain("Configuration total", text);
            Assert.DoesNotContain("Discount Terms", text);
            Assert.DoesNotContain(request.DiscountTerms, text);
            Assert.DoesNotContain(page.Letters, IsFlareRed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiscountEmailExplainsFireplaceOnlyPricingAndEncodesMultilineTerms()
    {
        var request = Request(Fireplace("DVFF60H", 8_000m, 8_000m, 10m));
        request.DiscountTerms = "Pay within 10 days & confirm <delivery>.\r\n<script>alert('x')</script>\rFinal line.";
        var html = Email(request);
        Assert.Contains("MSRP is shown for reference.", html);
        Assert.Contains("The fireplace discount and new fireplace cost are shown separately in the attached quote.", html);
        Assert.Contains("Optional features, media, and any included outdoor kit remain at MSRP.", html);
        Assert.Contains("<strong>Discount Terms:</strong><br>Pay within 10 days &amp; confirm &lt;delivery&gt;.<br>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;<br>Final line.", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("\r", html);
        Assert.Contains("<a href=\"https://flarefireplaces.com/product\">Product Sheet</a>", html);
        Assert.EndsWith("<p>Sales signature</p>", html);
    }

    [Fact]
    public void DiscountEmailWithBlankTermsOmitsEmptyTermsHeading()
    {
        var request = Request(Fireplace("DVFF60H", 8_000m, 8_000m, 10m));
        request.DiscountTerms = " \r\n ";
        var html = Email(request);
        Assert.Contains("MSRP is shown for reference.", html);
        Assert.DoesNotContain("Discount Terms", html);
    }

    [Fact]
    public void EmailWithoutDiscountKeepsExistingOpeningAndOmitsUnusedTerms()
    {
        var request = Request(Fireplace("DVFF60H", 8_000m, 8_000m, 0m));
        request.DiscountTerms = "Unused discount terms.";
        var html = Email(request);
        Assert.StartsWith("<strong><em>Taylor,</em></strong><br>Below are links to the product information, including a quote for the fireplace and its optional features. " +
                          "The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 days, and do not include installation costs.<br><br><br>" +
                          "If you have any questions or are ready to proceed,", html);
        Assert.DoesNotContain("Discount Terms", html);
        Assert.DoesNotContain(request.DiscountTerms, html);
        Assert.DoesNotContain("new fireplace cost", html);
    }

    private static PricedFireplaceQuote Fireplace(string model, decimal baseMsrp, decimal eligibleMsrp, decimal percent) => new()
    {
        Type = FireplaceType.Indoor,
        FireplaceLabel = model,
        Model = model,
        ModelNumber = model,
        Description = model + " fireplace",
        DiscountPercent = percent,
        DiscountEligibleMsrp = eligibleMsrp,
        BaseLine = new PriceLine { Feature = "Fireplace", Sku = model, Description = model + " fireplace", Price = baseMsrp }
    };

    private static QuoteRequest Request(params PricedFireplaceQuote[] fireplaces)
    {
        var request = new QuoteRequest
        {
            ClientName = "Taylor",
            ProjectName = "Discount Quote Rendering",
            QuoteDate = "10/21/2026",
            QuoteNumber = "Q-DISCOUNT-TEST"
        };
        request.Tag = new PricedQuoteResult { Success = true, Request = request, Fireplaces = fireplaces.ToList() };
        return request;
    }

    private static string Email(QuoteRequest request) => new EmailTemplateService().BuildHtml(
        request, (PricedQuoteResult)request.Tag!,
        [new ResourceLinkSet { ModelNumber = "DVFF60H", Links = { ["Product Sheet"] = "https://flarefireplaces.com/product" } }],
        new AppSettings(), "<p>Sales signature</p>");

    private static bool IsFlareRed(Letter letter)
    {
        var (red, green, blue) = letter.Color.ToRGBValues();
        return Math.Abs(red - 231d / 255d) < .005 &&
               Math.Abs(green - 75d / 255d) < .005 && Math.Abs(blue - 75d / 255d) < .005;
    }

    private static string PageText(Page page) => Regex.Replace(ContentOrderTextExtractor.GetText(page), @"\s+", " ").Trim();
    private static string Compact(string text) => Regex.Replace(text, @"\s+", string.Empty);
    private static string TemporaryPdfPath() => Path.Combine(Path.GetTempPath(), $"flare-discount-rendering-{Guid.NewGuid():N}.pdf");
}
