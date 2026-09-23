using FlareQuotes.Core.Email;
using Xunit;

namespace FlareQuotes.Tests.EmailTests;

public sealed class EmailAddressNormalizerTests
{
    [Theory]
    [InlineData("customer@example.com", "customer@example.com")]
    [InlineData("  mailto:customer@example.com  ", "customer@example.com")]
    [InlineData("Test Customer <customer@example.com>", "customer@example.com")]
    [InlineData("customer@example.com.", "customer@example.com")]
    public void NormalizesCommonCopiedRecipientFormats(string input, string expected)
    {
        Assert.True(EmailAddressNormalizer.TryNormalizeSingle(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemovesInvisibleAndFullWidthCharacters()
    {
        const string input = "cus\u200Btomer＠example．com\u00A0";

        Assert.True(EmailAddressNormalizer.TryNormalizeSingle(input, out var actual));
        Assert.Equal("customer@example.com", actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("first@example.com second@example.com")]
    public void RejectsMissingInvalidOrMultipleSingleRecipients(string input)
    {
        Assert.False(EmailAddressNormalizer.TryNormalizeSingle(input, out _));
    }

    [Fact]
    public void NormalizesAndDeduplicatesAddressLists()
    {
        Assert.True(EmailAddressNormalizer.TryNormalizeList(
            "Sales <sales@example.com>; sales@example.com, Quotes <quotes@example.com>", out var addresses));

        Assert.Equal(new[] { "sales@example.com", "quotes@example.com" }, addresses);
    }
}
