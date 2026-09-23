using FlareQuotes.Core.Models;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class AppSettingsDefaultsTests
{
    [Fact]
    public void NewSettingsUseOrganizationSafeCommunicationDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(string.Empty, settings.HubSpotBcc);
        Assert.Equal("https://flarefireplaces.com", settings.ConsultationUrl);
    }
}
