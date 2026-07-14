using System.Security.Cryptography;
using System.Text;
using FlareQuotes.Core.Updates;

namespace FlareQuotes.Infrastructure.Updates;

public static class ManifestSignatureVerifier
{
    public static bool Validate(UpdateManifest manifest, string? publicKeyPem, bool strict, out string status)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            status = strict ? "Manifest signature is required but missing."
                            : "Manifest is unsigned. SHA-256 installer validation will still run.";

            return !strict;
        }

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            status = strict ? "Manifest public key is required but missing."
                            : "Manifest signature present, but no public key is configured.";

            return !strict;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            var payload = BuildSignedPayload(manifest);
            var signature = Convert.FromBase64String(manifest.Signature);

            var valid = rsa.VerifyData(Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256,
                                       RSASignaturePadding.Pkcs1);

            status = valid ? "Manifest signature verified." : "Manifest signature verification failed.";
            return valid;
        }
        catch (Exception ex)
        {
            status = "Manifest signature could not be verified: " + ex.GetBaseException().Message;
            return !strict;
        }
    }

    public static string BuildSignedPayload(UpdateManifest manifest)
    {
        var installerUrl = string.IsNullOrWhiteSpace(manifest.Url) ? manifest.Installer : manifest.Url;

        return string.Join("\n", new[] { manifest.Version?.Trim() ?? string.Empty, installerUrl?.Trim() ?? string.Empty,
                                         manifest.Sha256?.Trim().ToLowerInvariant() ?? string.Empty,
                                         manifest.Notes?.Trim() ?? string.Empty });
    }
}