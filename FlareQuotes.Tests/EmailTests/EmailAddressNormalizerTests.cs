using FlareQuotes.Core.Email;
using Xunit;

namespace FlareQuotes.Tests.EmailTests;

public sealed class EmailAddressNormalizerTests
{
    [Theory]
    [InlineData("phildaloisio@gmail.com", "phildaloisio@gmail.com")]
    [InlineData("  mailto:phildaloisio@gmail.com  ", "phildaloisio@gmail.com")]
    [InlineData("Phil Daloisio <phildaloisio@gmail.com>", "phildaloisio@gmail.com")]
    [InlineData("phildaloisio@gmail.com.", "phildaloisio@gmail.com")]
    public void NormalizesCommonCopiedRecipientFormats(string input, string expected)
    {
        Assert.True(EmailAddressNormalizer.TryNormalizeSingle(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemovesInvisibleAndFullWidthCharacters()
    {
        const string input = "phil\u200Bdaloisio＠gmail．com\u00A0";

        Assert.True(EmailAddressNormalizer.TryNormalizeSingle(input, out var actual));
        Assert.Equal("phildaloisio@gmail.com", actual);
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
