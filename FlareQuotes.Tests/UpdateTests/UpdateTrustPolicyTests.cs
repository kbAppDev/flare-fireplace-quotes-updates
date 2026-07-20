using FlareQuotes.Core.Updates;
using Xunit;

namespace FlareQuotes.Tests.UpdateTests;

public sealed class UpdateTrustPolicyTests
{
    [Fact]
    public void AcceptsOnlyThePinnedManifestAndMatchingReleaseAsset()
    {
        Assert.True(UpdateTrustPolicy.TryGetTrustedManifestUri(UpdateTrustPolicy.ManifestUrl, out _));
        Assert.False(UpdateTrustPolicy.TryGetTrustedManifestUri(
                         "https://example.com/flare-quotes-v1-latest.json", out _));

        const string trustedInstaller =
            "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/download/v1.4.10/Flare.Fireplace.Quotes.exe";

        Assert.True(UpdateTrustPolicy.TryGetTrustedInstallerUri(trustedInstaller, "1.4.10", out _));
        Assert.False(UpdateTrustPolicy.TryGetTrustedInstallerUri(
                         "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/download/v1.4.8/Flare.Fireplace.Quotes.exe",
                         "1.4.10", out _));
        Assert.False(UpdateTrustPolicy.TryGetTrustedInstallerUri(
                         "https://example.com/Flare.Fireplace.Quotes.exe", "1.4.10", out _));
    }

    [Fact]
    public void RejectsMalformedVerificationMetadata()
    {
        Assert.True(UpdateTrustPolicy.IsValidVersion("1.4.10"));
        Assert.False(UpdateTrustPolicy.IsValidVersion("v1.4.10"));
        Assert.True(UpdateTrustPolicy.IsValidSha256(new string('a', 64)));
        Assert.False(UpdateTrustPolicy.IsValidSha256("abc"));
        Assert.True(UpdateTrustPolicy.IsValidInstallerSize(90 * 1024 * 1024));
        Assert.False(UpdateTrustPolicy.IsValidInstallerSize(0));
        Assert.False(UpdateTrustPolicy.IsValidInstallerSize(UpdateTrustPolicy.MaxInstallerBytes + 1));
    }
}
