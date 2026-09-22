using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Excel;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class PassiveHeatFlexFramingResourceTests
{
    private const string PassiveGuideLabel = "Passive Heat Flex Framing Guide";
    private const string PassiveGuideRoot =
        "https://flarefireplaces.com/wp-content/uploads/Data/PassiveHF/Framing/";

    public static TheoryData<FireplaceType, string, string, string, string> PassiveGuideCases =>
        new()
        {
            { FireplaceType.Indoor, "FF", "60", "16", "framing-FF-wood.pdf" },
            { FireplaceType.Indoor, "FF", "60", "24", "framing-FF-H-wood.pdf" },
            { FireplaceType.Indoor, "FF", "60", "30", "framing-FF-EH-wood.pdf" },
            { FireplaceType.IndoorSeeThrough, "ST", "60", "16", "framing-ST-wood.pdf" },
            { FireplaceType.IndoorSeeThrough, "ST", "60", "24", "framing-ST-H-wood.pdf" },
            { FireplaceType.IndoorSeeThrough, "ST", "60", "30", "framing-ST-EH-wood.pdf" },
            { FireplaceType.Indoor, "LC", "60", "16", "framing-LC-wood.pdf" },
            { FireplaceType.Indoor, "LC", "60", "24", "framing-LC-H-wood.pdf" },
            { FireplaceType.Indoor, "LC", "60", "30", "framing-LC-EH-wood.pdf" },
            { FireplaceType.Indoor, "RC", "60", "16", "framing-RC-wood.pdf" },
            { FireplaceType.Indoor, "RC", "60", "24", "framing-RC-H-wood.pdf" },
            { FireplaceType.Indoor, "RC", "60", "30", "framing-RC-EH-wood.pdf" },
            { FireplaceType.Indoor, "DC", "60", "16", "framing-DC-wood.pdf" },
            { FireplaceType.Indoor, "DC", "60", "24", "framing-DC-H-wood.pdf" },
            { FireplaceType.Indoor, "DC", "60", "30", "framing-DC-EH-wood.pdf" },
            { FireplaceType.Indoor, "RD", "60", "16", "framing-RD-wood.pdf" },
            { FireplaceType.Indoor, "RD", "60", "24", "framing-RD-H-wood.pdf" },
            { FireplaceType.Indoor, "RD", "60", "30", "framing-RD-EH-wood.pdf" },
            { FireplaceType.Indoor, "FFPASS", "30", "60", "framing-PASS-FF-wood.pdf" },
            { FireplaceType.IndoorSeeThrough, "STPASS", "30", "60", "framing-PASS-ST-wood.pdf" },
            {
                FireplaceType.IndoorOutdoorSeeThrough, "STPASSIO", "30", "60",
                "framing-PASS-ST-IO-wood.pdf"
            },
            { FireplaceType.IndoorOutdoorSeeThrough, "STIO", "60", "16", "framing-ST-OD-wood.pdf" },
            { FireplaceType.IndoorOutdoorSeeThrough, "STIO", "60", "24", "framing-ST-OD-H-wood.pdf" },
            { FireplaceType.IndoorOutdoorSeeThrough, "STIO", "60", "30", "framing-ST-OD-EH-wood.pdf" }
        };

    [Theory]
    [MemberData(nameof(PassiveGuideCases))]
    public async Task PassiveHeatFlexReplacesStandardFramingWithTheExactGuideForTheConfiguration(
        FireplaceType type,
        string model,
        string size,
        string glassHeight,
        string expectedFileName)
    {
        var service = new ClosedXmlPriceBookService();
        var resourceSet = Assert.Single(
            await service.ResolveResourceLinksAsync(
                BuildRequest(type, model, size, glassHeight, includePassiveHeatFlex: true),
                PricingPath()));

        var passiveGuide = Assert.Single(
            resourceSet.Links,
            pair => pair.Key.Equals(PassiveGuideLabel, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(PassiveGuideRoot + expectedFileName, passiveGuide.Value);
        Assert.DoesNotContain(resourceSet.Links.Keys, IsStandardFramingLabel);
        Assert.Equal("specific", resourceSet.Sources[PassiveGuideLabel], ignoreCase: true);
    }

    [Fact]
    public async Task FreeFlowConfigurationKeepsItsStandardWoodAndMetalFramingGuides()
    {
        var service = new ClosedXmlPriceBookService();
        var resourceSet = Assert.Single(
            await service.ResolveResourceLinksAsync(
                BuildRequest(FireplaceType.Indoor, "FF", "60", "24", includePassiveHeatFlex: false),
                PricingPath()));

        Assert.Equal(
            "https://flarefireplaces.com/wp-content/uploads/Data/FF/framing-FF-H-wood.pdf",
            resourceSet.Links["Wood Framing"]);
        Assert.Equal(
            "https://flarefireplaces.com/wp-content/uploads/Data/FF/framing-FF-H-metal.pdf",
            resourceSet.Links["Metal Framing"]);
        Assert.DoesNotContain(
            resourceSet.Links.Keys,
            key => key.Equals(PassiveGuideLabel, StringComparison.OrdinalIgnoreCase));
    }

    private static QuoteRequest BuildRequest(
        FireplaceType type,
        string model,
        string size,
        string glassHeight,
        bool includePassiveHeatFlex)
    {
        var fireplace = new FireplaceQuote
        {
            Type = type,
            Model = model,
            Size = size,
            GlassHeight = glassHeight
        };

        if (includePassiveHeatFlex)
        {
            fireplace.Features.Add(
                new FeatureSelection
                {
                    Key = "passive_heat_flex",
                    DisplayName = "Passive Heat Flex",
                    PdfDescription = "Keep it Simple Ducted Heat Release Register and Plenum"
                });
        }

        return new QuoteRequest { Fireplaces = [fireplace] };
    }

    private static bool IsStandardFramingLabel(string label) =>
        label.Equals("Wood Framing", StringComparison.OrdinalIgnoreCase) ||
        label.Equals("Metal Framing", StringComparison.OrdinalIgnoreCase) ||
        label.Equals("Framing Guide", StringComparison.OrdinalIgnoreCase);

    private static string PricingPath()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "LocalData", "pricing.xlsx");
        Assert.True(File.Exists(path), $"Pricing workbook missing: {path}");
        Assert.True(
            File.Exists(Path.Combine(root, "LocalData", "resource_links.xlsx")),
            "Resource links workbook is required for this regression test.");
        return path;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "FlareQuotes.App")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
