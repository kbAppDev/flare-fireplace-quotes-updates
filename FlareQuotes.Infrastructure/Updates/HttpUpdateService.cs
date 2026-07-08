using System.Text.Json;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Updates;

namespace FlareQuotes.Infrastructure.Updates;

public sealed class HttpUpdateService : IUpdateService
{
    private const string DefaultManifestUrl = "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IAppLogger? _logger;
    private readonly ISettingsService? _settingsService;

    public HttpUpdateService(IAppLogger? logger = null, ISettingsService? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Flare Fireplace Quotes Updater/1.3.4");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Pragma", "no-cache");
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _settingsService is null
                ? null
                : await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

            var manifestUrl = string.IsNullOrWhiteSpace(settings?.UpdateManifestUrl)
                ? DefaultManifestUrl
                : settings.UpdateManifestUrl;

            var json = await _httpClient.GetStringAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                return new UpdateCheckResult { Message = "The update manifest could not be read." };

            var strictSignatureValidation = settings?.StrictManifestSignatureValidation == true;
            var publicKeyPem = settings?.UpdateManifestPublicKeyPem;

            if (!ManifestSignatureVerifier.Validate(manifest, publicKeyPem, strictSignatureValidation, out var signatureStatus))
            {
                _logger?.Warning(signatureStatus);

                return new UpdateCheckResult
                {
                    Message = "The update manifest could not be verified."
                };
            }

            if (signatureStatus.Contains("unsigned", StringComparison.OrdinalIgnoreCase))
                _logger?.Warning(signatureStatus);
            else
                _logger?.Info(signatureStatus);

            var updateAvailable = IsNewer(manifest.Version, currentVersion);
            var installerUrl = string.IsNullOrWhiteSpace(manifest.Url) ? manifest.Installer : manifest.Url;

            return new UpdateCheckResult
            {
                UpdateAvailable = updateAvailable,
                LatestVersion = manifest.Version,
                InstallerUrl = installerUrl,
                Sha256 = manifest.Sha256,
                Notes = manifest.Notes,
                Message = updateAvailable ? $"Version {manifest.Version} is available." : "No update available."
            };
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Update check failed.");
            return new UpdateCheckResult { Message = "Update check failed. Please try again later." };
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        return Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;
    }
}