using System.Xml.Linq;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class UiAccessibilityContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void MainQuoteActionsExposeStableAccessibleNames()
    {
        var document = LoadView("MainWindow.xaml");

        AssertAutomationName(document, "ThemeToggleButton", "Switch light or dark theme");
        AssertAutomationName(document, "RecallLastQuoteButton", "Open recent quotes");
        AssertAutomationName(document, "AddFireplaceButton", "Add or save current fireplace");
        AssertAutomationName(document, "DecreaseFireplaceQuantityButton", "Decrease fireplace quantity");
        AssertAutomationName(document, "FireplaceQuantityValue", "Fireplace quantity");
        AssertAutomationName(document, "IncreaseFireplaceQuantityButton", "Increase fireplace quantity");
        AssertAutomationName(document, "FeatureDropdownButton", "Select additional features");
        AssertAutomationName(document, "GeneratePreviewButton", "Generate quote preview");
    }

    [Fact]
    public void SettingsActionsExposeStableAccessibleNames()
    {
        var document = LoadView("SettingsWindow.xaml");

        AssertAutomationName(document, "RunSystemHealthButton", "Run security and system health check");
        AssertAutomationName(document, "SettingsCancelButton", "Cancel settings changes");
        AssertAutomationName(document, "SettingsSaveButton", "Save settings");
    }

    private static XDocument LoadView(string fileName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "FlareQuotes.App", "Views", fileName);
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FlareQuotes.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for the XAML contract test.");
    }

    private static void AssertAutomationName(XDocument document, string elementName, string expectedName)
    {
        var element = document.Descendants().SingleOrDefault(candidate =>
            string.Equals((string?)candidate.Attribute(XamlNamespace + "Name"), elementName,
                          StringComparison.Ordinal));
        Assert.NotNull(element);
        Assert.Equal(expectedName, (string?)element.Attribute("AutomationProperties.Name"));
    }
}
