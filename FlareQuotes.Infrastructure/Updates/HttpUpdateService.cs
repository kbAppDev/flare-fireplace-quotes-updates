using System.Text.Json;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Updates;

namespace FlareQuotes.Infrastructure.Updates;

public sealed class HttpUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly IAppLogger? _logger;
    private readonly ISettingsService? _settingsService;

    public HttpUpdateService(IAppLogger? logger = null, ISettingsService? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;

        var version = typeof(HttpUpdateService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                                                                  $"Flare-Fireplace-Quotes-Updater/{version}");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Pragma", "no-cache");
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion,
                                                    CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _settingsService is null
                               ? null
                               : await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

            var configuredUrl = string.IsNullOrWhiteSpace(settings?.UpdateManifestUrl)
                                    ? UpdateTrustPolicy.ManifestUrl
                                    : settings.UpdateManifestUrl;

            if (!UpdateTrustPolicy.TryGetTrustedManifestUri(configuredUrl, out var manifestUri))
            {
                _logger?.Warning("Update check blocked because the manifest URL is outside the trusted release lane.");
                return new UpdateCheckResult { Message = "The configured update source is not trusted." };
            }

            var manifest = await DownloadManifestAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            if (manifest is null || !UpdateTrustPolicy.IsValidVersion(manifest.Version))
                return new UpdateCheckResult { Message = "The update manifest version is invalid." };

            var strictSignatureValidation = settings?.StrictManifestSignatureValidation == true;
            var publicKeyPem = settings?.UpdateManifestPublicKeyPem;

            if (!ManifestSignatureVerifier.Validate(manifest, publicKeyPem, strictSignatureValidation,
                                                    out var signatureStatus))
            {
                _logger?.Warning(signatureStatus);
                return new UpdateCheckResult { Message = "The update manifest could not be verified." };
            }

            if (signatureStatus.Contains("unsigned", StringComparison.OrdinalIgnoreCase))
                _logger?.Warning(signatureStatus);
            else
                _logger?.Info(signatureStatus);

            var installerUrl = string.IsNullOrWhiteSpace(manifest.Url) ? manifest.Installer : manifest.Url;
            if (!UpdateTrustPolicy.TryGetTrustedInstallerUri(installerUrl, manifest.Version, out var installerUri))
            {
                _logger?.Warning("Update check blocked an installer URL outside the trusted GitHub release asset.");
                return new UpdateCheckResult { Message = "The update installer location is not trusted." };
            }

            if (!UpdateTrustPolicy.IsValidSha256(manifest.Sha256))
                return new UpdateCheckResult { Message = "The update manifest SHA-256 value is invalid." };

            if (!UpdateTrustPolicy.IsValidInstallerSize(manifest.SizeBytes))
                return new UpdateCheckResult { Message = "The update manifest installer size is invalid." };

            if ((manifest.Notes?.Length ?? 0) > 12000)
                return new UpdateCheckResult { Message = "The update manifest release notes are invalid." };

            var updateAvailable = IsNewer(manifest.Version, currentVersion);

            return new UpdateCheckResult {
                UpdateAvailable = updateAvailable,
                LatestVersion = manifest.Version,
                InstallerUrl = installerUri.AbsoluteUri,
                Sha256 = manifest.Sha256.Trim().ToLowerInvariant(),
                ExpectedSizeBytes = manifest.SizeBytes,
                Notes = manifest.Notes ?? string.Empty,
                Message = updateAvailable ? $"Version {manifest.Version} is available." : "No update available."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Update check failed.");
            return new UpdateCheckResult { Message = "Update check failed. Please try again later." };
        }
    }

    private async Task<UpdateManifest?> DownloadManifestAsync(Uri manifestUri,
                                                               CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                                                        cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (!UpdateTrustPolicy.IsTrustedDownloadResponseUri(response.RequestMessage?.RequestUri))
            throw new InvalidDataException("Update manifest redirected to an untrusted host.");

        if (response.Content.Headers.ContentLength is > UpdateTrustPolicy.MaxManifestBytes)
            throw new InvalidDataException("Update manifest exceeds the maximum allowed size.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;

            if (destination.Length + count > UpdateTrustPolicy.MaxManifestBytes)
                throw new InvalidDataException("Update manifest exceeds the maximum allowed size.");

            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }

        return JsonSerializer.Deserialize<UpdateManifest>(
            destination.ToArray(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 12 });
    }

    private static bool IsNewer(string latest, string current)
    {
        return UpdateTrustPolicy.IsValidVersion(latest) && Version.TryParse(latest, out var l) &&
               Version.TryParse(current, out var c) && l > c;
    }
}
