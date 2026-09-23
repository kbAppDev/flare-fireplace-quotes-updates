using FlareQuotes.Infrastructure.Logging;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class RedactingFileLoggerTests
{
    [Theory]
    [InlineData("\"access_token\": \"abc123\"")]
    [InlineData("refresh_token=abc123")]
    [InlineData("Authorization: Bearer abc.def.ghi")]
    [InlineData("Bearer abc.def.ghi")]
    public void RedactsTokenShapes(string source)
    {
        var redacted = RedactingFileLogger.Redact(source);

        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.ghi", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactsEmailsAndLocalUserPaths()
    {
        const string source =
            "customer@example.com / (214) 555-0199 / D:\\Users\\Kyle\\Documents\\quote.pdf / " +
            "\\\\office-server\\profiles\\Users\\Taylor\\quote.pdf";

        var redacted = RedactingFileLogger.Redact(source);

        Assert.DoesNotContain("customer@example.com", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("214", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kyle", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Taylor", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactsCustomerNameFromQuotePdfPaths()
    {
        const string source =
            "Could not open 'C:\\Users\\Kyle\\AppData\\Local\\Flare Fireplace Quotes\\Temp\\" +
            "Flare Fireplace Quote - DVFF60H - Jane Smith.pdf'.";

        var redacted = RedactingFileLogger.Redact(source);

        Assert.DoesNotContain("Jane Smith", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DVFF60H", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppData", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[path]", redacted, StringComparison.Ordinal);
    }
}
