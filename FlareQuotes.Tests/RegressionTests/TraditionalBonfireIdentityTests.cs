using FlareQuotes.Core.Models;
using FlareQuotes.Core.Parsing;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class TraditionalBonfireIdentityTests
{
    [Theory]
    [InlineData("TRA-BON-42", "42")]
    [InlineData("TRA-BON-46", "46")]
    [InlineData("TRABON42", "42")]
    [InlineData("Flare-TRA-BON-46", "46")]
    [InlineData("BONTR42", "42")]
    [InlineData("tra bon 46", "46")]
    public void AutoFillKeepsBonfireIdentityAndInfersSize(string code, string size)
    {
        var parser = new DefaultQuoteRequestParser();
        foreach (var text in new[] { code, $"Model: {code}", $"Please quote {code} for the living room." })
        {
            var request = parser.Parse(text);
            Assert.Equal("Traditional Bonfire", request.Model);
            Assert.Equal(size, request.Size);
            Assert.Equal(FireplaceType.Traditional, FireplaceModelClassifier.DetectType(request.Model, request.Size));
            Assert.Empty(request.GlassHeight);
        }
        Assert.True(TraditionalFireplaceModel.IsBonfire(code));
        Assert.Equal(FireplaceType.Traditional, FireplaceModelClassifier.DetectType(code));
    }

    [Theory]
    [InlineData("TR-42", "42")]
    [InlineData("TR-46", "46")]
    [InlineData("DVTRA42", "42")]
    public void StandardTraditionalModelsRemainDistinct(string code, string size)
    {
        var request = new DefaultQuoteRequestParser().Parse(code);
        Assert.Equal("Traditional", request.Model);
        Assert.Equal(size, request.Size);
        Assert.False(TraditionalFireplaceModel.IsBonfire(code));
        Assert.False(TraditionalFireplaceModel.IsBonfire(request.Model));
    }

    [Fact]
    public void FallbackProjectTitleIncludesBonfire()
    {
        var title = QuoteProjectTitleFormatter.BuildFireplaceTitle(new PricedFireplaceQuote
        {
            Type = FireplaceType.Traditional,
            ModelNumber = "TRA-BON-46",
            Model = "Traditional Bonfire",
            Size = "46",
            FireplaceLocation = "Living Room"
        });
        Assert.Equal("Living Room — Indoor Traditional Bonfire 46\"", title);
    }
}
