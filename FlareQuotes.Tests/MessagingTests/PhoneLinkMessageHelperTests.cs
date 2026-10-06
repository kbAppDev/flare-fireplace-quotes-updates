using FlareQuotes.Core.Messaging;
using Xunit;

namespace FlareQuotes.Tests.MessagingTests;

public sealed class PhoneLinkMessageHelperTests
{
    [Theory]
    [InlineData("(312) 555-0184", "3125550184")]
    [InlineData(" +1 (312) 555-0184 ", "+13125550184")]
    [InlineData("312.555.0184", "3125550184")]
    [InlineData("020 7946 0958", "02079460958")]
    public void PhoneNumbersNormalizeWithoutGuessingCountryCode(string input, string expected)
    {
        Assert.True(PhoneLinkMessageHelper.TryNormalizePhone(input, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("123")]
    [InlineData("3125550184 ext 7")]
    [InlineData("1+3125550184")]
    [InlineData("3125550184;body=send")]
    [InlineData("1234567890123456")]
    public void BlankIncompleteOrAnnotatedNumbersAreNotReadyToCopy(string? input)
    {
        Assert.False(PhoneLinkMessageHelper.TryNormalizePhone(input, out var number));
        Assert.Empty(number);
    }

    [Fact]
    public void PhoneLinkLaunchOpensTheDocumentedAppWithoutRecipientOrMessageData()
    {
        Assert.Equal("ms-settings:mobile-devices-addphone-direct", PhoneLinkMessageHelper.LaunchUri);
        Assert.DoesNotContain("?", PhoneLinkMessageHelper.LaunchUri);
    }
}
