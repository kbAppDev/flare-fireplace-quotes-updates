namespace FlareQuotes.Core.Updates;

public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string Installer { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    // Optional professional-hardening fields.
    // Existing unsigned manifests still work unless StrictManifestSignatureValidation is enabled.
    public string Signature { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = "RS256";
}