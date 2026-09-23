using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace FlareQuotes.Tests.PdfTests;

public sealed class QuotePdfSmokeTests
{
    private const string ApprovedUrl = "https://flarefireplaces.com/passive-heat-flex/";
    private const string UntrustedUrl = "https://untrusted.example.test/should-not-appear";
    private const string BrandingEmail = "release169@example.com";
    private const string BrandingPhone = "(555) 016-9000";
    private const string BrandingWebsite = "quote-qa.flarefireplaces.com/v169";

    [Fact]
    public async Task SingleFireplacePdfIsReadableAndPreservesQuoteDataPricingAndSafeLinks()
    {
        var outputPath = TemporaryPdfPath();

        try
        {
            var request = BuildRequest(
                BuildFireplace("Living Room", "DVFF60H", quantity: 3, baseTotal: 12_345m, featureTotal: 450m));

            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(
                request, outputPath, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 1_000, "The generated PDF should contain real page data.");

            using var pdf = PdfDocument.Open(outputPath);
            Assert.Equal(1, pdf.NumberOfPages);

            var page = pdf.GetPage(1);
            AssertPdfText(page.Text, "Hardening Test Customer", "Q-169-SMOKE", BrandingEmail, BrandingPhone,
                          BrandingWebsite, "DVFF60H", "3", "$12,345", "$450");

            var hyperlinks = page.GetHyperlinks().Select(link => link.Uri).ToList();
            Assert.Contains(ApprovedUrl, hyperlinks);
            Assert.DoesNotContain(UntrustedUrl, hyperlinks);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Fact]
    public async Task MultipleFireplacesProduceOneValidPagePerConfiguration()
    {
        var outputPath = TemporaryPdfPath();

        try
        {
            var first = BuildFireplace("Great Room", "DVFF50", quantity: 1, baseTotal: 8_000m,
                                       featureTotal: 200m);
            var second = BuildFireplace("Primary Suite", "DVFF72H", quantity: 2, baseTotal: 20_000m,
                                        featureTotal: 700m);
            var request = BuildRequest(first, second);

            await new QuestPdfQuotePdfService().BuildQuotePdfAsync(
                request, outputPath, TestContext.Current.CancellationToken);

            using var pdf = PdfDocument.Open(outputPath);
            Assert.Equal(2, pdf.NumberOfPages);

            var firstPage = pdf.GetPage(1);
            var secondPage = pdf.GetPage(2);
            AssertPdfText(firstPage.Text, "Hardening Test Customer", "Q-169-SMOKE", "DVFF50", "$8,000");
            AssertPdfText(secondPage.Text, "Hardening Test Customer", "Q-169-SMOKE", "DVFF72H", "2", "$20,000",
                          "$700");

            Assert.DoesNotContain("DVFF72H", firstPage.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("DVFF50", secondPage.Text, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    private static QuoteRequest BuildRequest(params PricedFireplaceQuote[] fireplaces)
    {
        var priced = new PricedQuoteResult
        {
            Success = true,
            Fireplaces = fireplaces.ToList(),
            ResourceLinks = fireplaces.Select(
                                          fireplace => new ResourceLinkSet
                                          {
                                              ModelNumber = fireplace.ModelNumber,
                                              FireplaceLocation = fireplace.FireplaceLocation
                                          })
                                      .ToList()
        };

        var request = new QuoteRequest
        {
            ClientName = "Hardening Test Customer",
            ProjectName = "Offline PDF Verification",
            ProjectAddress = "100 Test Lane",
            QuoteDate = "09/23/2026",
            QuoteNumber = "Q-169-SMOKE",
            Branding = new QuoteBranding
            {
                SalesEmail = BrandingEmail,
                SalesPhone = BrandingPhone,
                Website = "https://" + BrandingWebsite
            },
            Fireplaces = fireplaces.Select(
                                       fireplace => new FireplaceQuote
                                       {
                                           FireplaceLocation = fireplace.FireplaceLocation,
                                           ModelNumber = fireplace.ModelNumber,
                                           Quantity = fireplace.Quantity
                                       })
                                   .ToList(),
            Tag = priced
        };
        priced.Request = request;
        return request;
    }

    private static PricedFireplaceQuote BuildFireplace(string location, string modelNumber, int quantity,
                                                        decimal baseTotal, decimal featureTotal) => new()
                                                        {
                                                            FireplaceLabel = modelNumber,
                                                            FireplaceLocation = location,
                                                            ProjectName = "Offline PDF Verification",
                                                            ProjectAddress = "100 Test Lane",
                                                            Type = FireplaceType.Indoor,
                                                            Model = "Front Facing",
                                                            ModelNumber = modelNumber,
                                                            Description = $"{modelNumber} test fireplace",
                                                            Quantity = quantity,
                                                            LeadTime = "3-5 Business Days",
                                                            BaseLine = new PriceLine
                                                            {
                                                                Feature = "Fireplace",
                                                                Sku = modelNumber,
                                                                Description = $"{modelNumber} test fireplace",
                                                                Quantity = quantity,
                                                                Price = baseTotal,
                                                                Url = UntrustedUrl
                                                            },
                                                            OptionalFeatures =
        [
            new PriceLine
            {
                Feature = "Passive Heat Flex",
                Description = "Approved product information",
                Quantity = quantity,
                Price = featureTotal,
                Url = ApprovedUrl
            },
            new PriceLine
            {
                Feature = "Untrusted Fixture",
                Description = "Link must be removed",
                Quantity = quantity,
                Price = 25m * quantity,
                Url = UntrustedUrl
            }
        ]
                                                        };

    private static void AssertPdfText(string actual, params string[] expectedValues)
    {
        foreach (var expected in expectedValues)
            Assert.Contains(expected, actual, StringComparison.Ordinal);
    }

    private static string TemporaryPdfPath() =>
        Path.Combine(Path.GetTempPath(), $"flare-quote-pdf-smoke-{Guid.NewGuid():N}.pdf");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A cleanup failure should not mask the PDF assertion that produced the test result.
        }
    }
}
