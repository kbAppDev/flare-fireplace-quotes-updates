using System.Text.Json;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private const string CorrectManifestUrl = "https://github.com/kbAppDev/flare-fireplace-quotes-updates/releases/latest/download/flare-quotes-v1-latest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonSettingsService(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? GetDefaultSettingsPath()
            : settingsPath;

        MigrateLegacySettingsIfNeeded();
    }

    private static string GetDefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flare Fireplace Quotes",
            "settings.json");
    }

    private static IEnumerable<string> LegacySettingsPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(local, "Flare Fireplace Quotes", "settings.json");
        yield return Path.Combine(local, "Flare Fireplaces - Quotes", "settings.json");
        yield return Path.Combine(local, "Flare Fireplaces - Quotes", "v3", "settings.json");
        yield return Path.Combine(local, "Flare Quote Builder", "settings.json");
    }

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

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);

        AppSettingsRuntimeCache.Set(normalized);

        return Task.CompletedTask;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl) ||
            settings.UpdateManifestUrl.Contains("flare-quotes-v1-flare-quotes-v1", StringComparison.OrdinalIgnoreCase) ||
            !settings.UpdateManifestUrl.Equals(CorrectManifestUrl, StringComparison.OrdinalIgnoreCase))
        {
            settings.UpdateManifestUrl = CorrectManifestUrl;
        }

        return settings;
    }

    private void TryBackupCorruptSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var backupPath = _settingsPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_settingsPath, backupPath, overwrite: false);
        }
        catch
        {
            // Ignore backup failures.
        }
    }
}








