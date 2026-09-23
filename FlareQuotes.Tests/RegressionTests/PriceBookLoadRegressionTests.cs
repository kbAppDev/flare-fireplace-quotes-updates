using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class PriceBookLoadRegressionTests
{
    [Fact]
    public async Task LoadAsync_ReadsNumericPriceByValueAcrossCultures()
    {
        var path = CreateWorkbook(cell =>
        {
            cell.Value = 1234.56m;
            cell.Style.NumberFormat.Format = "$#,##0.00";
        });

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);
            var row = Assert.Single(workbook.Rows);

            Assert.True(row.Price.HasValue);
            Assert.Equal(1234.56m, row.Price.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_PreservesEnUsTextPriceFallback()
    {
        var path = CreateWorkbook(cell => cell.Value = "$1,234.56");

        try
        {
            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);
            var row = Assert.Single(workbook.Rows);

            Assert.True(row.Price.HasValue);
            Assert.Equal(1234.56m, row.Price.Value);
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyWorkbookForCorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flare-corrupt-price-book-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(path, "This is not an XLSX package.");

        try
        {
            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Equal(path, workbook.SourcePath);
            Assert.Empty(workbook.SheetNames);
            Assert.Empty(workbook.Rows);
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsWorkbookAboveCompressedSizeLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flare-oversized-price-book-{Guid.NewGuid():N}.xlsx");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(ClosedXmlPriceBookService.MaximumWorkbookBytes + 1);

        try
        {
            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Empty(workbook.SheetNames);
            Assert.Empty(workbook.Rows);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsSuspiciousCompressionRatio()
    {
        var path = CreateNamedWorkbook("ZIP-BOMB", 100m);

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry("xl/media/repeated.bin", CompressionLevel.SmallestSize);
                using var stream = entry.Open();
                var repeatedBytes = new byte[2 * 1024 * 1024];
                stream.Write(repeatedBytes);
            }

            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Empty(workbook.Rows);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsExternalWorkbookParts()
    {
        var path = CreateNamedWorkbook("EXTERNAL-LINK", 100m);

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry("xl/externalLinks/externalLink1.xml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<externalLink />");
            }

            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Empty(workbook.Rows);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsExternalWorkbookRelationships()
    {
        var path = CreateNamedWorkbook("EXTERNAL-RELATIONSHIP", 100m);

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry("xl/worksheets/_rels/injected.xml.rels");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" " +
                    "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink\" " +
                    "Target=\"externalLinks/externalLink1.xml\"/></Relationships>");
            }

            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Empty(workbook.Rows);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsWorksheetDimensionsOutsideOperationalLimits()
    {
        var path = CreateNamedWorkbook("OVERSIZED-DIMENSION", 100m);

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                archive.GetEntry("xl/worksheets/sheet1.xml")!.Delete();
                var worksheet = archive.CreateEntry("xl/worksheets/sheet1.xml");
                using var writer = new StreamWriter(worksheet.Open());
                writer.Write(
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<dimension ref=\"A1:XFD1048576\"/><sheetData/></worksheet>");
            }

            var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

            Assert.Empty(workbook.Rows);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_PropagatesCancellation()
    {
        var path = CreateNamedWorkbook("CANCEL", 100m);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new ClosedXmlPriceBookService().LoadAsync(path, cancellation.Token));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ParsesTheSameImmutableSnapshotThatPassedValidation()
    {
        var path = CreateNamedWorkbook("VALIDATED-SNAPSHOT", 100m);
        var replacementPath = CreateNamedWorkbook("UNVALIDATED-REPLACEMENT", 999m);
        using (var archive = ZipFile.Open(replacementPath, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("xl/externalLinks/externalLink1.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<externalLink />");
        }

        var callbackCount = 0;
        var service = new ClosedXmlPriceBookService(
            () =>
            {
                Interlocked.Increment(ref callbackCount);
                File.Move(replacementPath, path, overwrite: true);
            });

        try
        {
            var workbook = await service.LoadAsync(path);

            Assert.Equal(1, callbackCount);
            var row = Assert.Single(workbook.Rows);
            Assert.Equal("VALIDATED-SNAPSHOT", row.Sku);
            Assert.Equal(100m, row.Price);
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            DeleteIfPresent(path);
            DeleteIfPresent(replacementPath);
        }
    }

    [Theory]
    [InlineData("pricing.xlsx")]
    [InlineData("resource_links.xlsx")]
    [InlineData("outdoor_spec_center_extracted_links.xlsx")]
    public async Task LoadAsync_AcceptsShippedWorkbooks(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LocalData", fileName);

        Assert.True(File.Exists(path), $"Expected shipped workbook at {path}.");
        Assert.True(ClosedXmlPriceBookService.IsXlsxPackage(path));

        var workbook = await new ClosedXmlPriceBookService().LoadAsync(path);

        Assert.NotEmpty(workbook.SheetNames);
    }

    [Fact]
    public async Task LoadAsync_CachesPricingAndResourceWorkbooksIndependently()
    {
        var pricingPath = CreateNamedWorkbook("PRICING-A", 100m);
        var resourcePath = CreateNamedWorkbook("RESOURCE-B", 200m);

        try
        {
            var service = new ClosedXmlPriceBookService();
            var pricingFirst = await service.LoadAsync(pricingPath);
            var resource = await service.LoadAsync(resourcePath);
            var pricingSecond = await service.LoadAsync(pricingPath);

            Assert.Same(pricingFirst, pricingSecond);
            Assert.NotSame(pricingFirst, resource);
            Assert.Equal("PRICING-A", Assert.Single(pricingSecond.Rows).Sku);
            Assert.Equal("RESOURCE-B", Assert.Single(resource.Rows).Sku);
        }
        finally
        {
            DeleteIfPresent(pricingPath);
            DeleteIfPresent(resourcePath);
        }
    }

    [Fact]
    public async Task LoadAsync_ReplacesCachedWorkbookWhenSamePathChanges()
    {
        var path = CreateNamedWorkbook("ORIGINAL", 100m);

        try
        {
            var service = new ClosedXmlPriceBookService();
            var original = await service.LoadAsync(path);

            ReplaceWorkbook(path, "REPLACEMENT-WITH-DIFFERENT-LENGTH", 999m);
            var replacement = await service.LoadAsync(path);

            Assert.NotSame(original, replacement);
            var row = Assert.Single(replacement.Rows);
            Assert.Equal("REPLACEMENT-WITH-DIFFERENT-LENGTH", row.Sku);
            Assert.Equal(999m, row.Price);
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public async Task InvalidateCache_ForcesRequestedWorkbookToReload()
    {
        var path = CreateNamedWorkbook("INVALIDATE", 300m);

        try
        {
            var service = new ClosedXmlPriceBookService();
            var first = await service.LoadAsync(path);

            service.InvalidateCache(path);
            var second = await service.LoadAsync(path);

            Assert.NotSame(first, second);
            Assert.Equal("INVALIDATE", Assert.Single(second.Rows).Sku);
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    private static string CreateWorkbook(Action<IXLCell> setPrice)
    {
        var path = Path.Combine(Path.GetTempPath(), $"flare-price-book-{Guid.NewGuid():N}.xlsx");

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Indoor");
        worksheet.Cell(1, 1).Value = "SKU";
        worksheet.Cell(1, 2).Value = "MSRP";
        worksheet.Cell(2, 1).Value = "TEST-PRICE";
        setPrice(worksheet.Cell(2, 2));
        workbook.SaveAs(path);

        return path;
    }

    private static string CreateNamedWorkbook(string sku, decimal price)
    {
        var path = Path.Combine(Path.GetTempPath(), $"flare-price-book-{Guid.NewGuid():N}.xlsx");
        WriteNamedWorkbook(path, sku, price);
        return path;
    }

    private static void ReplaceWorkbook(string path, string sku, decimal price)
    {
        var replacementPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}-replacement.xlsx");
        WriteNamedWorkbook(replacementPath, sku, price);
        File.Move(replacementPath, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }

    private static void WriteNamedWorkbook(string path, string sku, decimal price)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Indoor");
        worksheet.Cell(1, 1).Value = "SKU";
        worksheet.Cell(1, 2).Value = "MSRP";
        worksheet.Cell(2, 1).Value = sku;
        worksheet.Cell(2, 2).Value = price;
        workbook.SaveAs(path);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
