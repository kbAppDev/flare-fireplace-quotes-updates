namespace FlareQuotes.Core.Updates;

public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string Installer { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    // Unsigned manifests remain compatible unless strict validation is enabled. If a signature
    // is supplied, it must always be a verifiable RS256 signature; malformed signatures fail closed.
    public string Signature { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = "RS256";
}
