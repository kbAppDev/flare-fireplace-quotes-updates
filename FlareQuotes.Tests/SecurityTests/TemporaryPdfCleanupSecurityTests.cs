using System.Reflection;
using FlareQuotes.App.ViewModels;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class TemporaryPdfCleanupSecurityTests
{
    [Fact]
    public void AcceptsOnlyPdfDirectlyInsideRegularPreviewDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareTemporaryPdfSecurityTests", Guid.NewGuid().ToString("N"));
        var preview = Path.Combine(root, "preview-20260923-120000000");
        var nested = Path.Combine(preview, "nested");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(outside);

        try
        {
            var ownedPdf = Path.Combine(preview, "Flare Fireplace Quote - Test Customer.pdf");
            var nestedPdf = Path.Combine(nested, "nested.pdf");
            var outsidePdf = Path.Combine(outside, "outside.pdf");
            File.WriteAllText(ownedPdf, "%PDF");
            File.WriteAllText(nestedPdf, "%PDF");
            File.WriteAllText(outsidePdf, "%PDF");

            Assert.True(IsOwned(root, ownedPdf));
            Assert.False(IsOwned(root, nestedPdf));
            Assert.False(IsOwned(root, outsidePdf));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool IsOwned(string root, string candidate)
    {
        var method = typeof(MainViewModel).GetMethod(
            "TryResolveOwnedTemporaryQuotePdf", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = [root, candidate, null, null];
        return Assert.IsType<bool>(method.Invoke(null, arguments));
    }
}
