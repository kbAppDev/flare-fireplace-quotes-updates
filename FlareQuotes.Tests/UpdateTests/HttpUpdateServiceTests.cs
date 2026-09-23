using System.Net;
using System.Text;
using System.Text.Json;
using FlareQuotes.Core.Updates;
using FlareQuotes.Infrastructure.Updates;
using Xunit;

namespace FlareQuotes.Tests.UpdateTests;

public sealed class HttpUpdateServiceTests
{
    [Fact]
    public async Task AcceptsSignedManifestFromPinnedFeed()
    {
        var manifest = CreateSignedManifest();
        using var service = CreateService(manifest);

        var result = await service.CheckAsync("9.9.9");

        Assert.True(result.CheckSucceeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.Equal("No update available.", result.Message);
    }

    [Fact]
    public async Task RejectsUnsignedManifestFromPinnedFeed()
    {
        var manifest = CreateSignedManifest();
        manifest.Signature = string.Empty;
        using var service = CreateService(manifest);

        var result = await service.CheckAsync("1.0.0");

        Assert.False(result.CheckSucceeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("The update manifest could not be verified.", result.Message);
    }

    private static HttpUpdateService CreateService(UpdateManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest);
        return new HttpUpdateService(new StaticResponseHandler(json));
    }

    private static UpdateManifest CreateSignedManifest() => new()
    {
        Version = "9.9.9",
        Url =
            "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/download/v9.9.9/Flare.Fireplace.Quotes.exe",
        Sha256 = new string('a', 64),
        SizeBytes = 123456789,
        Notes = "Signed manifest fixture.",
        SignatureAlgorithm = "RS256",
        Signature =
            "VJ6Rb0Ej9d1iUaI5pyUP83XlB7INwKfPRLapw8kmKC1M38ousNuUB0ObhR5cQ2uosU7E7vSY6QAKWNFpuCejQZln+kzkkNAhxUusVoUUdHAZUbo43XdmI7gBir16ztEzN7rA6qsELNwT0iC5j+6ahLvnpstK/p0rz8vA3GxHbITMw5zEwBVB31lnUGzBQ77MICgaHxCeIVjq0guke3jacg7x1EwQ795RVHaFR+uvIkzSZZMbsOShJ++aiYdNevEA//knmivHWFd3xwdHR5/ngArPSvdcwZQKBDUb+nQMHcRrFQDJYbyNcwkLNcd4wkkXLKOL9NCHy8B8ZmVFxlFYchQaczIcvVyzDPsWRGeWnx/DVWCRvfXLt6YQpGAtGyYSGohYL+kX/uDPNUh7HzJb2b0oL300ucwBkHmuevYmG0BK1FKheLNZkk3sTEYj4L96XltX35bdDf36US+yPyynPrf/v+2H65LN6spuAbfv7StDbNLXqqtywuGUDZz27Efc"
    };

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
