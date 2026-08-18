using FlareQuotes.Core.Media;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.MediaTests;

public sealed class MediaSelectionServiceTests
{
    [Theory]
    [InlineData(FireplaceType.Outdoor)]
    [InlineData(FireplaceType.OutdoorSeeThrough)]
    public void OutdoorClassicMediaIncludesDiamondOptions(FireplaceType type)
    {
        var service = new MediaSelectionService();

        var keys = service.GetClassicMedia(type).Select(option => option.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("pd_black", keys);
        Assert.Contains("pd_rain", keys);
        Assert.Contains("fg_black", keys);
        Assert.DoesNotContain("cp_white", keys);
        Assert.DoesNotContain("gl_wood", keys);
    }
}
