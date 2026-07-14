using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using FlareQuotes.Tests.TestSupport;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class AllPriceBookEmailPathTests
{
    [Fact]
    public async Task EveryFireplaceModelBuildsSubjectHtmlAndResourceLinksWithoutThrowing()
    {
        var root = FindRepoRoot();
        var pricingPath = Path.Combine(root, "LocalData", "pricing.xlsx");
        Assert.True(File.Exists(pricingPath), $"Pricing workbook missing: {pricingPath}");

        var priceBook = new ClosedXmlPriceBookService();
        var workbook = await priceBook.LoadAsync(pricingPath);
        var models = PriceBookModelCatalog.GetFireplaceModels(workbook);
        var template = new EmailTemplateService();
        var settings = new AppSettings();
        var failures = new List<string>();

        Assert.NotEmpty(models);
        AssertInventoryMatches(root, models);
        var categories = models.Select(row => PriceBookModelCatalog.Category(row.Sku))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedCategories = new HashSet<string>(
            ["Indoor", "Indoor See Through", "Outdoor", "Outdoor See Through", "Large", "Traditional"],
            StringComparer.OrdinalIgnoreCase);
        Assert.Empty(expectedCategories.Except(categories, StringComparer.OrdinalIgnoreCase));

        foreach (var row in models)
        {
            try
            {
                var type = PriceBookModelCatalog.Type(row.Sku);
                var request = BuildRequest(row, type, "test@example.com");
                var priced = BuildPricedResult(row, type, request);
                var links = await priceBook.ResolveResourceLinksAsync(request, pricingPath);
                var subject = template.BuildSubject(request, priced);
                var html = template.BuildHtml(request, priced, links, settings, string.Empty);

                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(html))
                    failures.Add($"{row.Sku}: empty subject or HTML");
            }
            catch (Exception ex)
            {
                failures.Add($"{row.Sku}: {ex.GetBaseException().Message}");
            }
        }

        Assert.True(failures.Count == 0,
                    "Model-path failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    internal static QuoteRequest BuildRequest(PriceRow row, FireplaceType type, string email)
    {
        return new QuoteRequest { ProjectName = "Automated Model Test", ClientName = "Flare QA", Email = email,
                                  Model = row.Sku, Fireplaces = [new FireplaceQuote { Type = type, Model = row.Sku,
                                                                                      LeadTime = "TEST" }] };
    }

    internal static PricedQuoteResult BuildPricedResult(PriceRow row, FireplaceType type, QuoteRequest request)
    {
        return new PricedQuoteResult {
            Success = true, Request = request,
            Fireplaces = [new PricedFireplaceQuote {
                Type = type, Model = row.Sku, ModelNumber = row.Sku, Description = row.Description,
                BaseLine = new PriceLine { Sku = row.Sku, Description = row.Description, Price = row.Price }
            }]
        };
    }

    private static void AssertInventoryMatches(string root, IReadOnlyList<PriceRow> models)
    {
        var inventoryPath = Path.Combine(root, "LocalData", "expected-fireplace-model-inventory.csv");
        Assert.True(File.Exists(inventoryPath), $"Expected model inventory missing: {inventoryPath}");

        var expected = File.ReadLines(inventoryPath)
                           .Skip(1)
                           .Select(line => line.Split(',') [0].Trim())
                           .Where(model => !string.IsNullOrWhiteSpace(model))
                           .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = models.Select(row => row.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(302, expected.Count);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Empty(expected.Except(actual, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(actual.Except(expected, StringComparer.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "FlareQuotes.App")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
