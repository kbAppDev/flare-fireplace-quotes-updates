using FlareQuotes.Core.Security;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class TrustedExternalLinkPolicyTests
{
    [Theory]
    [InlineData("https://flarefireplaces.com/specs/fireplace.pdf")]
    [InlineData("https://www.flarefireplaces.com/passive-heat-flex/")]
    [InlineData("https://flareorder.com/products/123")]
    public void AcceptsApprovedHttpsLinks(string value)
    {
        Assert.True(TrustedExternalLinkPolicy.TryNormalize(value, out var normalized));
        Assert.StartsWith("https://", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://flarefireplaces.com/spec.pdf")]
    [InlineData("https://flarefireplaces.com:8443/spec.pdf")]
    [InlineData("https://user:password@flarefireplaces.com/spec.pdf")]
    [InlineData("https://flarefireplaces.com.attacker.example/spec.pdf")]
    [InlineData("https://meetings.hubspot.com/attacker/resource")]
    [InlineData("https://attacker.hubspot.com/spec.pdf")]
    [InlineData("https://example.com/spec.pdf")]
    [InlineData("httpx://flarefireplaces.com/spec.pdf")]
    [InlineData("not a URL")]
    public void RejectsUntrustedOrMalformedLinks(string value)
    {
        Assert.False(TrustedExternalLinkPolicy.TryNormalize(value, out _));
    }

    [Theory]
    [InlineData("https://meetings.hubspot.com/flare/jobsite-consultation")]
    [InlineData("https://flarefireplaces.com/contact/")]
    public void AcceptsExplicitConsultationHosts(string value)
    {
        Assert.True(TrustedExternalLinkPolicy.TryNormalizeConsultation(value, out var normalized));
        Assert.StartsWith("https://", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://attacker.hubspot.com/sample-user/jobsite-consultation")]
    [InlineData("https://meetings.hubspot.com.attacker.test/sample-user/jobsite-consultation")]
    [InlineData("https://user:password@meetings.hubspot.com/flare/jobsite-consultation")]
    public void RejectsUntrustedConsultationHosts(string value)
    {
        Assert.False(TrustedExternalLinkPolicy.TryNormalizeConsultation(value, out _));
    }
}
