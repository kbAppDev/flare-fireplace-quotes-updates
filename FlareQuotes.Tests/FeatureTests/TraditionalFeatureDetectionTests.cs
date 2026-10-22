using FlareQuotes.Core.Features;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.FeatureTests;

public sealed class TraditionalFeatureDetectionTests
{
    [Theory]
    [InlineData("Herringbone Red Brick", "herringbone_red_brick_traditional")]
    [InlineData("Herringbone Red Brick Traditional", "herringbone_red_brick_traditional")]
    [InlineData("Herringbone Natural Brick Traditional", "herringbone_natural_brick_traditional")]
    [InlineData("Herringbone Black Brick Traditional", "herringbone_black_brick_traditional")]
    [InlineData("Herringbone Light Stone Brick Traditional", "herringbone_light_stone_brick_traditional")]
    [InlineData("Herringbone Light Stone", "herringbone_light_stone_brick_traditional")]
    [InlineData("Offset Light Stone Brick Traditional", "offset_light_stone_brick_traditional")]
    [InlineData("Offset Light Stone", "offset_light_stone_brick_traditional")]
    [InlineData("HB-RD-TRA-46", "herringbone_red_brick_traditional")]
    [InlineData("HB-NT-TRA-42", "herringbone_natural_brick_traditional")]
    [InlineData("HB-BL-TRA-46", "herringbone_black_brick_traditional")]
    [InlineData("HB-LS-TRA-42", "herringbone_light_stone_brick_traditional")]
    public void SpecificFinishDoesNotAlsoSelectItsGenericAlias(string text, string expectedKey)
    {
        var result = new FeatureSelectionService().DetectFromText(text, FireplaceType.Traditional);

        Assert.Equal(expectedKey, Assert.Single(result).Key);
    }

    [Theory]
    [InlineData("RBTRA42", "reflective_black_interior")]
    [InlineData("RDTRA46", "offset_red_brick_traditional")]
    [InlineData("NTTRA42", "offset_natural_brick_traditional")]
    [InlineData("BLTRA46", "offset_black_brick_traditional")]
    [InlineData("LSTRA42", "offset_light_stone_brick_traditional")]
    [InlineData("HBRDTRA46", "herringbone_red_brick_traditional")]
    [InlineData("HBNTTRA42", "herringbone_natural_brick_traditional")]
    [InlineData("HBBLTRA46", "herringbone_black_brick_traditional")]
    [InlineData("HBLSTRA42", "herringbone_light_stone_brick_traditional")]
    [InlineData("rdtra 46", "offset_red_brick_traditional")]
    [InlineData("hb ls tra 46", "herringbone_light_stone_brick_traditional")]
    public void RecognizesSizedInteriorSkusAsCompleteTokens(string sku, string expectedKey)
    {
        var result = new FeatureSelectionService().DetectFromText($"Please include ({sku}).", FireplaceType.Traditional);

        Assert.Equal(expectedKey, Assert.Single(result).Key);
    }

    [Theory]
    [InlineData("prefixRDTRA46")]
    [InlineData("RDTRA46suffix")]
    [InlineData("RDTRA460")]
    [InlineData("HBLSTRA420")]
    [InlineData("RDTRA48")]
    public void SizedInteriorAliasesDoNotMatchInsideOtherTokens(string text)
    {
        Assert.Empty(new FeatureSelectionService().DetectFromText(text, FireplaceType.Traditional));
    }

    [Theory]
    [InlineData("Herringbone Red Brick Traditional and Offset Red Brick Traditional",
        "offset_red_brick_traditional", "herringbone_red_brick_traditional")]
    [InlineData("Herringbone red brick for the first fireplace; red brick for the second fireplace",
        "offset_red_brick_traditional", "herringbone_red_brick_traditional")]
    [InlineData("HB-RD-TRA-46 plus RD-TRA-46", "offset_red_brick_traditional", "herringbone_red_brick_traditional")]
    [InlineData("HBLSTRA42 and LSTRA42", "offset_light_stone_brick_traditional", "herringbone_light_stone_brick_traditional")]
    public void IndependentlyWrittenFinishChoicesRemainSelected(string text, string firstKey, string secondKey)
    {
        var result = new FeatureSelectionService().DetectFromText(text, FireplaceType.Traditional);

        Assert.Equal(new[] { firstKey, secondKey }.OrderBy(key => key), result.Select(option => option.Key).OrderBy(key => key));
    }

    [Fact]
    public void FreeFlowAirIntakePhraseDoesNotAlsoSelectHeatReleaseLouver()
    {
        var service = new FeatureSelectionService();
        foreach (var text in new[] { "Free Flow Air Intake Louver", "free-flow air-intake" })
            Assert.Equal("air_intake_louver", Assert.Single(service.DetectFromText(text, FireplaceType.Traditional)).Key);
    }

    [Fact]
    public void GenericFreeFlowAndLegacyHeatReleaseLabelsIdentifyTheSameOption()
    {
        var service = new FeatureSelectionService();
        foreach (var text in new[] { "Free Flow", "Free Flow Louver", "Heat Release Louver" })
            Assert.Equal("heat_release_louver", Assert.Single(service.DetectFromText(text, FireplaceType.Traditional)).Key);
    }

    [Fact]
    public void IndependentlyRequestedFreeFlowAndAirIntakeLouversRemainSeparate()
    {
        var result = new FeatureSelectionService().DetectFromText(
            "Free Flow Louver for heat release and Free Flow Air Intake Louver", FireplaceType.Traditional);

        Assert.Equal(new[] { "air_intake_louver", "heat_release_louver" },
            result.Select(option => option.Key).OrderBy(key => key));
    }

    [Fact]
    public void AnalogTimerSkuAndDescriptionSelectOneTraditionalOptionWithoutSubstringMatches()
    {
        var service = new FeatureSelectionService();
        var result = service.DetectFromText("Please include AWALLTIMER (Analog Wall Timer).", FireplaceType.Traditional);

        Assert.Equal("analog_wall_timer", Assert.Single(result).Key);
        Assert.Empty(service.DetectFromText("MYAWALLTIMERREPLACEMENT", FireplaceType.Traditional));
        Assert.Empty(service.DetectFromText("AWALLTIMER", FireplaceType.Outdoor));
    }

    [Fact]
    public void DigitalOrUnspecifiedWallTimerDoesNotSubstituteTheAnalogTimer()
    {
        var service = new FeatureSelectionService();

        Assert.Empty(service.DetectFromText("Digital Wall Timer", FireplaceType.Traditional));
        Assert.Empty(service.DetectFromText("Wall Timer", FireplaceType.Traditional));
    }

    [Fact]
    public void TraditionalSkuAliasesRemainUnavailableForOtherFireplaceTypes()
    {
        Assert.Empty(new FeatureSelectionService().DetectFromText("RDTRA46 HBLSTRA42", FireplaceType.Indoor));
    }
}


