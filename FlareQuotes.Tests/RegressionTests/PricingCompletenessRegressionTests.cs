using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using FlareQuotes.Infrastructure.Pdf;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class PricingCompletenessRegressionTests
{
    [Fact]
    public async Task BuildPricedQuoteAsync_FailsWhenSelectedFeatureHasNoPrice()
    {
        var request = BuildKnownFireplaceRequest();
        request.Fireplaces[0].Features.Add(
            new FeatureSelection
            {
                Key = "missing_feature",
                DisplayName = "Unpriced Selected Feature",
                PdfDescription = "Selected by the user"
            });

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unpriced Selected Feature", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(result.Fireplaces).OptionalFeatures.Single().Price);
    }

    [Fact]
    public async Task BuildPricedQuoteAsync_FailsWhenSelectedChargeableMediaHasNoPrice()
    {
        var request = BuildKnownFireplaceRequest();
        request.Fireplaces[0].PremiumMedia.Add(
            new MediaSelection
            {
                Key = "missing_media",
                DisplayName = "Mystery Option QZXJ",
                IsPremium = true
            });

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Mystery Option QZXJ", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(result.Fireplaces).OptionalFeatures.Single().Price);
    }

    [Fact]
    public async Task BuildPricedQuoteAsync_DoesNotRequirePriceForIncludedClassicMedia()
    {
        var request = BuildKnownFireplaceRequest();
        request.ClassicMedia.Add(
            new MediaSelection
            {
                Key = "fg_black",
                DisplayName = "Black Fire Glass",
                IsPremium = false
            });

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        var fireplace = Assert.Single(result.Fireplaces);
        Assert.Equal("Black Fire Glass", fireplace.ClassicMediaDisplay);
        Assert.Empty(fireplace.OptionalFeatures);
    }

    [Fact]
    public async Task BuildPricedQuoteAsync_FailsWhenBaseFireplaceHasNoPrice()
    {
        var request = new QuoteRequest
        {
            Model = "Unknown Fireplace",
            Size = "999",
            GlassHeight = "16",
            Fireplaces =
            [
                new FireplaceQuote
                {
                    Type = FireplaceType.Indoor,
                    Model = "Unknown Fireplace",
                    Size = "999",
                    GlassHeight = "16"
                }
            ]
        };

        var result = await new ClosedXmlPriceBookService().BuildPricedQuoteAsync(request, PricingPath(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("base fireplace", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(result.Fireplaces).BaseLine.Price);
    }

    [Fact]
    public async Task PdfService_RejectsIncompletePricingSnapshot()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"flare-incomplete-{Guid.NewGuid():N}.pdf");
        var request = new QuoteRequest
        {
            Tag = new PricedQuoteResult
            {
                Success = false,
                Message = "Missing price for selected feature."
            }
        };

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, outputPath, TestContext.Current.CancellationToken));

            Assert.Contains("could not be priced", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfService_PropagatesCancellationWithoutPublishingOutput()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"flare-cancelled-{Guid.NewGuid():N}.pdf");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new QuestPdfQuotePdfService().BuildQuotePdfAsync(
                    new QuoteRequest(), outputPath, cancellation.Token));

            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(outputPath)!, $"{Path.GetFileName(outputPath)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfService_PublishesCompletedPdfAtomically()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"flare-complete-{Guid.NewGuid():N}.pdf");
        var request = new QuoteRequest
        {
            QuoteNumber = "TEST-0001",
            Tag = new PricedQuoteResult { Success = true }
        };

        try
        {
            var result = await new QuestPdfQuotePdfService().BuildQuotePdfAsync(request, outputPath, TestContext.Current.CancellationToken);

            Assert.Equal(outputPath, result);
            Assert.True(new FileInfo(outputPath).Length > 100);
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(outputPath)!, $"{Path.GetFileName(outputPath)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static QuoteRequest BuildKnownFireplaceRequest()
    {
        var fireplace = new FireplaceQuote
        {
            Type = FireplaceType.Indoor,
            Model = "Front Facing",
            Size = "60",
            GlassHeight = "16"
        };

        return new QuoteRequest
        {
            Model = fireplace.Model,
            Size = fireplace.Size,
            GlassHeight = fireplace.GlassHeight,
            Fireplaces = [fireplace]
        };
    }

    private static string PricingPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "LocalData", "pricing.xlsx");
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The pricing workbook was not found for the regression test.");
    }
}
