using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class FireplaceModelClassifierTests
{
    [Theory]
    [InlineData("FF", "60", FireplaceType.Indoor)]
    [InlineData("See Through", "70", FireplaceType.IndoorSeeThrough)]
    [InlineData("ST-OD", "70", FireplaceType.IndoorOutdoorSeeThrough)]
    [InlineData("VFF60H", "60", FireplaceType.Outdoor)]
    [InlineData("VST60H", "60", FireplaceType.OutdoorSeeThrough)]
    [InlineData("DVTRA42", "42", FireplaceType.Traditional)]
    [InlineData("LDVFF", "120", FireplaceType.Large)]
    [InlineData("LDVST120", "120", FireplaceType.Unknown)]
    [InlineData("FFPASS", "60", FireplaceType.Indoor)]
    [InlineData("STPASS", "60", FireplaceType.IndoorSeeThrough)]
    public void ClassifiesSharedBusinessRules(string model, string size, FireplaceType expected)
    {
        Assert.Equal(expected, FireplaceModelClassifier.DetectType(model, size));
    }
}
