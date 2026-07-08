using FlareQuotes.Core.Features;
using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.FeatureTests;

public sealed class FeatureSelectionServiceTests
{
    [Fact]
    public void DetectsRequiredPastedFeatureAliases()
    {
        var service = new FeatureSelectionService();
        var result = service.DetectFromText("Please include Double Glass, RGB LEDs, Power Vent, Summer Kit, Summit Burner, and Reflective Sides.", FireplaceType.Indoor);
        var keys = result.Select(x => x.Key).ToHashSet();

        Assert.Contains("double_glass", keys);
        Assert.Contains("rgb_leds", keys);
        Assert.Contains("power_vent", keys);
        Assert.Contains("summer_kit", keys);
        Assert.Contains("summit_burner", keys);
        Assert.Contains("reflective_black_sides", keys);
    }

    [Fact]
    public void ReflectiveBackIsNotAvailableForSeeThroughTypes()
    {
        var service = new FeatureSelectionService();

        var indoorSeeThroughKeys = service.GetAvailableOptions(FireplaceType.IndoorSeeThrough).Select(x => x.Key).ToHashSet();
        var indoorOutdoorSeeThroughKeys = service.GetAvailableOptions(FireplaceType.IndoorOutdoorSeeThrough).Select(x => x.Key).ToHashSet();
        var outdoorSeeThroughKeys = service.GetAvailableOptions(FireplaceType.OutdoorSeeThrough).Select(x => x.Key).ToHashSet();

        Assert.DoesNotContain("reflective_black_back", indoorSeeThroughKeys);
        Assert.DoesNotContain("reflective_black_back", indoorOutdoorSeeThroughKeys);
        Assert.DoesNotContain("reflective_black_back", outdoorSeeThroughKeys);

        Assert.Contains("reflective_black_sides", indoorSeeThroughKeys);
        Assert.Contains("reflective_black_sides", indoorOutdoorSeeThroughKeys);
        Assert.Contains("reflective_black_sides", outdoorSeeThroughKeys);
    }
}

