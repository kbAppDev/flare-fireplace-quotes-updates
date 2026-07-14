namespace FlareQuotes.Core.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}
