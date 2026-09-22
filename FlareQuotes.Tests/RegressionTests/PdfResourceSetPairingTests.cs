using System.Reflection;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Pdf;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class PdfResourceSetPairingTests
{
    [Fact]
    public void DuplicateModelsUseResourceSetFromTheSameQuotePosition()
    {
        var standard = new ResourceLinkSet
        {
            ModelNumber = "DVFF60H",
            FireplaceLocation = "Living Room",
            Links = { ["Wood Framing"] = "https://example.com/standard.pdf" }
        };
        var passive = new ResourceLinkSet
        {
            ModelNumber = "DVFF60H",
            FireplaceLocation = "Bedroom",
            Links =
            {
                ["Passive Heat Flex Framing Guide"] =
                    "https://flarefireplaces.com/wp-content/uploads/Data/PassiveHF/Framing/framing-FF-H-wood.pdf"
            }
        };
        var duplicateModel = new PricedFireplaceQuote
        {
            ModelNumber = "DVFF60H",
            FireplaceLocation = "Bedroom"
        };
        var method = typeof(QuestPdfQuotePdfService).GetMethod(
            "ResolvePageResourceLinkSet",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var resolved = method!.Invoke(null, new object?[]
        {
            new List<ResourceLinkSet> { standard, passive }, duplicateModel, 1
        });

        Assert.Same(passive, resolved);
    }
}
