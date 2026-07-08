using FlareQuotes.Core.Parsing;
using Xunit;

namespace FlareQuotes.Tests.ParserTests;

public sealed class DefaultQuoteRequestParserTests
{
    [Fact]
    public void ParsesBasicStructuredRequest()
    {
        var parser = new DefaultQuoteRequestParser();
        var result = parser.Parse("""
            Project Name: Test Project
            Jane Smith
            jane@example.com
            512-555-1212
            Postal: 75001
            Model: Front Facing
            Size: 80"
            Glass Height: 30"
            Features: Power Vent, Summer Kit, RGB LEDs
            """);

        Assert.Equal("Test Project", result.ProjectName);
        Assert.Equal("jane@example.com", result.Email);
        Assert.Equal("(512) 555-1212", result.Phone);
        Assert.Equal("75001", result.Postal);
        Assert.Equal("Front Facing", result.Model);
        Assert.Equal("80", result.Size);
        Assert.Equal("30", result.GlassHeight);
    }
}
