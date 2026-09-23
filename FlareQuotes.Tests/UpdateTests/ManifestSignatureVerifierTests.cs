using System.Security.Cryptography;
using System.Text;
using FlareQuotes.Core.Updates;
using FlareQuotes.Infrastructure.Updates;
using Xunit;

namespace FlareQuotes.Tests.UpdateTests;

public sealed class ManifestSignatureVerifierTests
{
    [Fact]
    public void RejectsUnsignedManifest()
    {
        var manifest = CreateManifest();

        Assert.False(ManifestSignatureVerifier.Validate(manifest, out var status));
        Assert.Contains("required", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmbeddedReleaseKeyAcceptsKnownSignatureAndRejectsTampering()
    {
        var manifest = CreateManifest();
        manifest.Signature =
            "VJ6Rb0Ej9d1iUaI5pyUP83XlB7INwKfPRLapw8kmKC1M38ousNuUB0ObhR5cQ2uosU7E7vSY6QAKWNFpuCejQZln+kzkkNAhxUusVoUUdHAZUbo43XdmI7gBir16ztEzN7rA6qsELNwT0iC5j+6ahLvnpstK/p0rz8vA3GxHbITMw5zEwBVB31lnUGzBQ77MICgaHxCeIVjq0guke3jacg7x1EwQ795RVHaFR+uvIkzSZZMbsOShJ++aiYdNevEA//knmivHWFd3xwdHR5/ngArPSvdcwZQKBDUb+nQMHcRrFQDJYbyNcwkLNcd4wkkXLKOL9NCHy8B8ZmVFxlFYchQaczIcvVyzDPsWRGeWnx/DVWCRvfXLt6YQpGAtGyYSGohYL+kX/uDPNUh7HzJb2b0oL300ucwBkHmuevYmG0BK1FKheLNZkk3sTEYj4L96XltX35bdDf36US+yPyynPrf/v+2H65LN6spuAbfv7StDbNLXqqtywuGUDZz27Efc";

        Assert.True(ManifestSignatureVerifier.Validate(manifest, out _));

        manifest.SizeBytes++;
        Assert.False(ManifestSignatureVerifier.Validate(manifest, out _));
        manifest.SizeBytes--;
        manifest.Notes = "tampered";
        Assert.False(ManifestSignatureVerifier.Validate(manifest, out _));
    }

    [Fact]
    public void CryptographicVerifierAcceptsRs256AndRejectsInvalidMetadata()
    {
        using var rsa = RSA.Create(2048);
        var manifest = CreateManifest();
        manifest.Signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(ManifestSignatureVerifier.BuildSignedPayload(manifest)),
                         HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        Assert.True(ManifestSignatureVerifier.ValidateWithPublicKey(manifest, publicKey, out _));

        manifest.Signature = "not-base64";
        Assert.False(ManifestSignatureVerifier.ValidateWithPublicKey(manifest, publicKey, out _));

        manifest.SignatureAlgorithm = "none";
        Assert.False(ManifestSignatureVerifier.ValidateWithPublicKey(manifest, publicKey, out _));
    }

    private static UpdateManifest CreateManifest() => new()
    {
        Version = "9.9.9",
        Url =
            "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/download/v9.9.9/Flare.Fireplace.Quotes.exe",
        Sha256 = new string('a', 64),
        SizeBytes = 123456789,
        Notes = "Signed manifest fixture.",
        SignatureAlgorithm = "RS256"
    };
}
