using System.Text;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Settings;
using FlareQuotes.Infrastructure.Excel;
using FlareQuotes.Infrastructure.Gmail;
using FlareQuotes.Infrastructure.Logging;
using FlareQuotes.Infrastructure.Pdf;
using FlareQuotes.Tests.RegressionTests;
using FlareQuotes.Tests.TestSupport;
using Xunit;

namespace FlareQuotes.Tests.IntegrationTests;

public sealed class GmailEveryModelIntegrationTests
{
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PerModelTimeout = TimeSpan.FromSeconds(75);

    [Fact]
    public async Task CreateConfirmDeleteDraftForEveryFireplaceModel()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLARE_RUN_GMAIL_INTEGRATION"), "1",
                           StringComparison.Ordinal))
        {
            return;
        }

        using var overallTimeout = new CancellationTokenSource(OverallTimeout);
        var cancellationToken = overallTimeout.Token;
        var root = FindRepoRoot();
        var pricingPath = Path.Combine(root, "LocalData", "pricing.xlsx");
        var settingsService = new JsonSettingsService();
        var settings = await settingsService.LoadAsync(cancellationToken);
        AppPaths.ImportGmailCredentials(settings.GmailCredentialsPath);
        Assert.True(File.Exists(AppPaths.GmailCredentialsFile),
                    $"Gmail credentials missing: {AppPaths.GmailCredentialsFile}");

        var logger = new RedactingFileLogger();
        var gmail = new GmailDraftService(settingsService, logger);
        var sender = await gmail.GetSenderDisplayAsync(cancellationToken);
        Assert.True(System.Net.Mail.MailAddress.TryCreate(sender, out _),
                    "The connected Gmail account did not return a valid sender address.");
        var priceBook = new ClosedXmlPriceBookService();
        var workbook = await priceBook.LoadAsync(pricingPath, cancellationToken);
        var models = PriceBookModelCatalog.GetFireplaceModels(workbook);
        var template = new EmailTemplateService();
        var pdfService = new QuestPdfQuotePdfService();
        var reportPath = Path.Combine(AppPaths.Reports, "gmail-every-model-report.csv");
        var progressPath = Path.Combine(AppPaths.Reports, "gmail-every-model-progress.log");
        var results = new List<ModelTestResult>();

        Assert.NotEmpty(models);
        AssertInventoryMatches(root, models);
        var expectedCategories = new HashSet<string>(
            ["Indoor", "Indoor See Through", "Outdoor", "Outdoor See Through", "Large", "Traditional"],
            StringComparer.OrdinalIgnoreCase);
        var actualCategories =
            models.Select(row => PriceBookModelCatalog.Category(row.Sku)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Empty(expectedCategories.Except(actualCategories, StringComparer.OrdinalIgnoreCase));
        Directory.CreateDirectory(AppPaths.Reports);
        File.WriteAllText(reportPath,
                          "Model,Category,Result,DraftConfirmed,Deleted,PdfBytes,Error" + Environment.NewLine,
                          Encoding.UTF8);
        File.WriteAllText(progressPath, string.Empty, Encoding.UTF8);

        for (var index = 0; index < models.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = models[index];
            var type = PriceBookModelCatalog.Type(row.Sku);
            var category = PriceBookModelCatalog.Category(row.Sku);
            var pdfPath = Path.Combine(AppPaths.Temp, $"gmail-model-test-{SafeFileName(row.Sku)}.pdf");
            string draftId = string.Empty;
            var deleted = false;
            long pdfBytes = 0;

            using var perModelTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perModelTimeout.CancelAfter(PerModelTimeout);

            try
            {
                var request = AllPriceBookEmailPathTests.BuildRequest(row, type, sender);
                var priced = AllPriceBookEmailPathTests.BuildPricedResult(row, type, request);
                var links = await priceBook.ResolveResourceLinksAsync(request, pricingPath, perModelTimeout.Token);
                priced.ResourceLinks = links.ToList();
                request.Tag = priced;

                await pdfService.BuildQuotePdfAsync(request, pdfPath, perModelTimeout.Token);
                pdfBytes = new FileInfo(pdfPath).Length;

                var result = await gmail.CreateDraftAsync(
                    new EmailDraftRequest { ToEmail = sender,
                                            Subject =
                                                "[AUTOMATED TEST - DELETE] " + template.BuildSubject(request, priced),
                                            HtmlBody =
                                                template.BuildHtml(request, priced, links, settings, string.Empty),
                                            PdfAttachmentPath = pdfPath, OpenBrowserAfterCreate = false },
                    perModelTimeout.Token);

                Assert.True(result.Success && !string.IsNullOrWhiteSpace(result.DraftId), result.Message);
                draftId = result.DraftId;
                await gmail.DeleteDraftAsync(draftId, perModelTimeout.Token);
                deleted = true;

                var passed = new ModelTestResult(row.Sku, category, "PASS", true, true, pdfBytes, string.Empty);
                results.Add(passed);
                AppendReport(reportPath, passed);
                AppendProgress(progressPath, index + 1, models.Count, passed);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(draftId) && !deleted)
                    deleted = await TryDeleteDraftAsync(gmail, draftId);

                var failed = new ModelTestResult(row.Sku, category, "FAIL", !string.IsNullOrWhiteSpace(draftId),
                                                 deleted, pdfBytes, ex.GetBaseException().Message);

                results.Add(failed);
                AppendReport(reportPath, failed);
                AppendProgress(progressPath, index + 1, models.Count, failed);
            }
            finally
            {
                TryDeleteFile(pdfPath);
            }
        }

        WriteCategorySummary(results);
        var failures =
            results.Where(result => !string.Equals(result.Result, "PASS", StringComparison.Ordinal)).ToList();
        Assert.True(failures.Count == 0,
                    "Live Gmail model failures:" + Environment.NewLine +
                        string.Join(Environment.NewLine, failures.Select(result => $"{result.Model}: {result.Error}")));
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

    private static async Task<bool> TryDeleteDraftAsync(GmailDraftService gmail, string draftId)
    {
        try
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await gmail.DeleteDraftAsync(draftId, cleanupTimeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendReport(string path, ModelTestResult result)
    {
        File.AppendAllText(path,
                           Csv(result.Model, result.Category, result.Result,
                               result.DraftConfirmed.ToString().ToLowerInvariant(),
                               result.Deleted.ToString().ToLowerInvariant(), result.PdfBytes.ToString(), result.Error) +
                               Environment.NewLine,
                           Encoding.UTF8);
    }

    private static void AppendProgress(string path, int current, int total, ModelTestResult result)
    {
        File.AppendAllText(
            path, $"[{current}/{total}] {result.Result} | {result.Category} | {result.Model}{Environment.NewLine}",
            Encoding.UTF8);
    }

    private static void WriteCategorySummary(IEnumerable<ModelTestResult> results)
    {
        var lines = new List<string> { "Category,Models,Passed,Failed" };
        lines.AddRange(results.GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
                           .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                           .Select(group => Csv(group.Key, group.Count().ToString(),
                                                group.Count(result => result.Result == "PASS").ToString(),
                                                group.Count(result => result.Result != "PASS").ToString())));

        File.WriteAllLines(Path.Combine(AppPaths.Reports, "gmail-category-summary.csv"), lines, Encoding.UTF8);
    }

    private static string Csv(params string[] values) =>
        string.Join(',', values.Select(value => '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"'));

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Test cleanup failure is captured by the Gmail delete/report checks, not PDF cleanup.
        }
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

    private sealed record ModelTestResult(string Model, string Category, string Result, bool DraftConfirmed,
                                          bool Deleted, long PdfBytes, string Error);
}
