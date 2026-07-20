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
        const string source = "customer@example.com at C:\\Users\\Kyle\\Documents\\quote.pdf";

        var redacted = RedactingFileLogger.Redact(source);

        Assert.DoesNotContain("customer@example.com", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kyle", redacted, StringComparison.OrdinalIgnoreCase);
    }
}
