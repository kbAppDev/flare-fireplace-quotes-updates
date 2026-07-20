using System.Security.Cryptography;
using System.Text;
using FlareQuotes.Core.Updates;
using FlareQuotes.Infrastructure.Updates;
using Xunit;

namespace FlareQuotes.Tests.UpdateTests;

public sealed class ManifestSignatureVerifierTests
{
    [Fact]
    public void AllowsUnsignedManifestOnlyWhenStrictModeIsDisabled()
    {
        var manifest = CreateManifest();

        Assert.True(ManifestSignatureVerifier.Validate(manifest, null, strict: false, out _));
        Assert.False(ManifestSignatureVerifier.Validate(manifest, null, strict: true, out _));
    }

    [Fact]
    public void AcceptsValidRs256SignatureAndRejectsTampering()
    {
        using var rsa = RSA.Create(2048);
        var manifest = CreateManifest();
        manifest.Signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(ManifestSignatureVerifier.BuildSignedPayload(manifest)),
                         HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        Assert.True(ManifestSignatureVerifier.Validate(manifest, publicKey, strict: false, out _));

        manifest.SizeBytes++;
        Assert.False(ManifestSignatureVerifier.Validate(manifest, publicKey, strict: false, out _));
        manifest.SizeBytes--;
        manifest.Notes = "tampered";
        Assert.False(ManifestSignatureVerifier.Validate(manifest, publicKey, strict: false, out _));
    }

    [Fact]
    public void RejectsPresentButUnverifiableSignaturesEvenOutsideStrictMode()
    {
        var manifest = CreateManifest();
        manifest.Signature = "not-base64";

        Assert.False(ManifestSignatureVerifier.Validate(manifest, null, strict: false, out _));

        manifest.SignatureAlgorithm = "none";
        Assert.False(ManifestSignatureVerifier.Validate(manifest, "not-a-key", strict: false, out _));
    }

    private static UpdateManifest CreateManifest() => new() {
        Version = "1.4.11",
        Url =
            "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/download/v1.4.11/Flare.Fireplace.Quotes.exe",
        Sha256 = new string('a', 64),
        SizeBytes = 1024,
        Notes = "test",
        SignatureAlgorithm = "RS256"
    };
}
