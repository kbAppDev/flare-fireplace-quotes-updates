using System.IO.Compression;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class SharedWorkbookDownloadPolicyTests
{
    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/id/export?format=xlsx", true)]
    [InlineData("https://doc-00-sheets.googleusercontent.com/export/file.xlsx", true)]
    [InlineData("http://docs.google.com/spreadsheets/d/id/export?format=xlsx", false)]
    [InlineData("https://docs.google.com.example.com/file.xlsx", false)]
    [InlineData("https://docs.google.com:444/file.xlsx", false)]
    [InlineData("https://user@docs.google.com/file.xlsx", false)]
    [InlineData("https://example.com/file.xlsx", false)]
    public void RestrictsWorkbookRedirectsToHttpsGoogleHosts(string value, bool expected)
    {
        Assert.Equal(expected, ClosedXmlPriceBookService.IsTrustedSharedWorkbookResponseUri(new Uri(value)));
    }

    [Fact]
    public void RequiresCoreXlsxPackageEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flare-xlsx-policy-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                archive.CreateEntry("[Content_Types].xml");
                archive.CreateEntry("xl/workbook.xml");
            }

            Assert.True(ClosedXmlPriceBookService.IsXlsxPackage(path));

            File.WriteAllText(path, "not an xlsx package");
            Assert.False(ClosedXmlPriceBookService.IsXlsxPackage(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
