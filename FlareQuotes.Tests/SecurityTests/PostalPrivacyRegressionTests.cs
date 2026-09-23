using System.Reflection;
using FlareQuotes.App.ViewModels;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class PostalPrivacyRegressionTests
{
    [Fact]
    public async Task ZipOnlyProjectAddressRemainsLocalAndUnexpanded()
    {
        var method = typeof(MainViewModel).GetMethod("ResolveProjectAddressForQuoteAsync",
                                                     BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var task = Assert.IsType<Task<string>>(method.Invoke(null, ["60614", "ZIP: 60614"]));

        Assert.Equal("60614", await task);
    }
}
