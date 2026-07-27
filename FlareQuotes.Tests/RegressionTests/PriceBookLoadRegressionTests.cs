using System.Globalization;
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

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
