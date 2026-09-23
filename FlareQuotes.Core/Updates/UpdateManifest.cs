namespace FlareQuotes.Core.Updates;

public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string Installer { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    // Every production manifest must carry an RS256 signature from the private key whose public
    // half is compiled into UpdateTrustPolicy. Unsigned or incorrectly signed manifests fail closed.
    public string Signature { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = "RS256";
}
