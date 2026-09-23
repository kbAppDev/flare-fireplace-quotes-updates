using System.IO.Compression;
using ClosedXML.Excel;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Health;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class SystemHealthServiceTests
{
    [Fact]
    public void BundledWorkbooksHaveValidRuntimeSchemas()
    {
        var localData = Path.Combine(AppContext.BaseDirectory, "LocalData");

        var pricing = SystemHealthService.CheckPricingWorkbook(Path.Combine(localData, "pricing.xlsx"));
        var resources = SystemHealthService.CheckResourceLinksWorkbook(Path.Combine(localData, "resource_links.xlsx"));
        var outdoor = SystemHealthService.CheckOutdoorLinksWorkbook(
            Path.Combine(localData, "outdoor_spec_center_extracted_links.xlsx"));

        Assert.Equal(SystemHealthState.Ok, pricing.State);
        Assert.Equal(SystemHealthState.Ok, resources.State);
        Assert.Equal(SystemHealthState.Ok, outdoor.State);
    }

    [Fact]
    public void OutdoorWorkbookContainsOnlyRuntimeSheetAndColumns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LocalData", "outdoor_spec_center_extracted_links.xlsx");
        using var workbook = new XLWorkbook(path);

        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("App Resource Rows", sheet.Name);
        Assert.Equal(8, sheet.RangeUsed()!.ColumnCount());
        Assert.Equal(51, sheet.RangeUsed()!.RowCount());

        var allText = string.Join('\n', sheet.CellsUsed().Select(cell => cell.GetString()));
        Assert.DoesNotContain("wp-admin", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("avatar", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scrape", allText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptWorkbookIsReportedAsNeedsAttention()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareSystemHealthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outdoor.xlsx");
        File.WriteAllText(path, "not an xlsx file");

        try
        {
            var result = SystemHealthService.CheckOutdoorLinksWorkbook(path);

            Assert.Equal(SystemHealthState.Error, result.State);
            Assert.Contains("valid XLSX", result.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HighExpansionWorkbookIsRejectedByHealthCheckBeforeClosedXmlParsing()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareSystemHealthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pricing.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Indoor");
            sheet.Cell("A1").Value = "SKU";
            sheet.Cell("B1").Value = "MSRP";
            sheet.Cell("A2").Value = "TEST-50";
            sheet.Cell("B2").Value = 100m;
            workbook.SaveAs(path);
        }

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("xl/media/high-expansion.bin", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(new byte[2 * 1024 * 1024]);
        }

        try
        {
            var result = SystemHealthService.CheckPricingWorkbook(path);

            Assert.Equal(SystemHealthState.Error, result.State);
            Assert.Contains("safety", result.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OutdoorWorkbookWithScrapeSheetIsReportedAsNeedsAttention()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareSystemHealthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "outdoor.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var runtime = workbook.AddWorksheet("App Resource Rows");
            runtime.Cell("A1").InsertData(new[]
            {
                new[] { "Model Key", "Compact Key", "Product Sheet", "Framing Guide", "Dimension File", "CAD", "SketchUp", "Revit" },
                new[] { "VFF-50", "VFF50", "https://example.test/product", "https://example.test/frame", "https://example.test/dimensions", "https://example.test/cad", "https://example.test/sketchup", "https://example.test/revit" }
            });
            workbook.AddWorksheet("All URLs").Cell("A1").Value = "wp-admin";
            workbook.SaveAs(path);
        }

        try
        {
            var result = SystemHealthService.CheckOutdoorLinksWorkbook(path);

            Assert.Equal(SystemHealthState.Error, result.State);
            Assert.Contains("unexpected worksheets", result.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
