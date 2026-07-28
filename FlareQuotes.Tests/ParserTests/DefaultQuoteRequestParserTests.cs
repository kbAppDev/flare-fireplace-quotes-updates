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

    [Fact]
    public void NormalizesCopiedEmailCharactersDuringAutoFillParsing()
    {
        var parser = new DefaultQuoteRequestParser();
        var result = parser.Parse("""
            Project Name: Hidden Character Test
            Name: Phil Daloisio
            Email: phil​daloisio＠gmail．com 
            Model: FF
            Size: 60
            Glass Height: 24
            """);

        Assert.Equal("phildaloisio@gmail.com", result.Email);
    }

    [Theory]
    [InlineData("VFDC50H", "Outdoor Vent Free Double Corner", "50", "24")]
    [InlineData("VFLC100", "Outdoor Vent Free Left Corner", "100", "16")]
    [InlineData("VFFF80H", "Outdoor Vent Free Front Facing", "80", "24")]
    [InlineData("LDVFF140H", "Large Front Facing", "140", "24")]
    public void DecodesStandaloneCompleteFireplaceCodes(
        string rawCode,
        string expectedModel,
        string expectedSize,
        string expectedGlassHeight)
    {
        var parser = new DefaultQuoteRequestParser();

        var result = parser.Parse(rawCode);

        Assert.Equal(expectedModel, result.Model);
        Assert.Equal(expectedSize, result.Size);
        Assert.Equal(expectedGlassHeight, result.GlassHeight);
    }

    [Theory]
    [InlineData("DVFF50HC")]
    [InlineData("DVST80EC")]
    public void DoesNotDecodeDiscontinuedCommercialCodes(string commercialCode)
    {
        var parser = new DefaultQuoteRequestParser();

        var standalone = parser.Parse(commercialCode);
        var labeled = parser.Parse($"Model: {commercialCode}");

        Assert.True(string.IsNullOrWhiteSpace(standalone.Model));
        Assert.True(string.IsNullOrWhiteSpace(standalone.Size));
        Assert.True(string.IsNullOrWhiteSpace(standalone.GlassHeight));
        Assert.Equal(commercialCode, labeled.Model);
        Assert.True(string.IsNullOrWhiteSpace(labeled.Size));
        Assert.True(string.IsNullOrWhiteSpace(labeled.GlassHeight));
    }

}
