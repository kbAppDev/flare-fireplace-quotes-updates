using System.Text.Json;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Updates;

namespace FlareQuotes.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private const long MaximumSettingsBytes = 1024 * 1024;
    private const int MaximumSettingsBackups = 3;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly string _settingsPath;

    public JsonSettingsService(string? settingsPath = null)
    {
        AppPaths.MigrateLegacyData();
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? AppPaths.SettingsFile : settingsPath;
        MigrateLegacySettingsIfNeeded();
    }

    private static IEnumerable<string> LegacySettingsPaths() =>
        AppPaths.LegacyRoots.Select(root => Path.Combine(root, "settings.json"));

    private void MigrateLegacySettingsIfNeeded()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(_settingsPath))
                return;

            foreach (var legacyPath in LegacySettingsPaths())
            {
                if (string.Equals(legacyPath, _settingsPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(legacyPath))
                    continue;

                if (new FileInfo(legacyPath).Length <= MaximumSettingsBytes)
                    File.Copy(legacyPath, _settingsPath, overwrite: false);
                return;
            }
        }
        catch
        {
            // Settings should never prevent startup.
        }
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(_settingsPath))
            {
                var defaults = Normalize(new AppSettings());
                AppSettingsRuntimeCache.Set(defaults);
                return Task.FromResult(defaults);
            }

            if (new FileInfo(_settingsPath).Length > MaximumSettingsBytes)
                throw new InvalidDataException("Settings file exceeds the maximum allowed size.");

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var normalized = Normalize(settings);

            AppSettingsRuntimeCache.Set(normalized);
            return Task.FromResult(normalized);
        }
        catch
        {
            TryBackupCorruptSettings();

            var defaults = Normalize(new AppSettings());
            AppSettingsRuntimeCache.Set(defaults);
            return Task.FromResult(defaults);
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = Normalize(settings ?? new AppSettings());

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var tempPath = _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        AppSettingsRuntimeCache.Set(normalized);

        return Task.CompletedTask;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl) ||
            settings.UpdateManifestUrl.Contains("flare-quotes-v1-flare-quotes-v1",
                                                StringComparison.OrdinalIgnoreCase) ||
            !settings.UpdateManifestUrl.Equals(UpdateTrustPolicy.ManifestUrl, StringComparison.OrdinalIgnoreCase))
        {
            settings.UpdateManifestUrl = UpdateTrustPolicy.ManifestUrl;
        }

        settings.SalesEmail = TrimTo(settings.SalesEmail, 320);
        settings.SalesPhone = TrimTo(settings.SalesPhone, 64);
        settings.Website = TrimTo(settings.Website, 2048);
        settings.HubSpotBcc = TrimTo(settings.HubSpotBcc, 1000);
        settings.ConsultationUrl = TrimTo(settings.ConsultationUrl, 2048);
        settings.PricingFile = TrimTo(settings.PricingFile, 32767);
        settings.GmailCredentialsPath = TrimTo(settings.GmailCredentialsPath, 32767);
        settings.UpdateManifestPublicKeyPem = TrimTo(settings.UpdateManifestPublicKeyPem, 16384);
        settings.RecallQuoteHistoryLimit = Math.Clamp(settings.RecallQuoteHistoryLimit, 1, 20);

        var presets = (settings.LeadTimePresets ?? [])
            .Select(value => TrimTo(value, 80))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
        settings.LeadTimePresets = presets.Count > 0 ? presets : new AppSettings().LeadTimePresets;

        return settings;
    }

    private static string TrimTo(string? value, int maximumLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private void TryBackupCorruptSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            if (new FileInfo(_settingsPath).Length > MaximumSettingsBytes)
                return;

            var backupPath = _settingsPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_settingsPath, backupPath, overwrite: false);

            var pattern = Path.GetFileName(_settingsPath) + ".bak-*";
            foreach (var staleBackup in Directory.EnumerateFiles(Path.GetDirectoryName(_settingsPath)!, pattern)
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(MaximumSettingsBackups))
            {
                File.Delete(staleBackup);
            }
        }
        catch
        {
            // Ignore backup failures.
        }
    }
}
