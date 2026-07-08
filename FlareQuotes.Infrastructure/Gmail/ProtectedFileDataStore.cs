using System.Security.Cryptography;
using System.Text;
using Google.Apis.Util.Store;
using Newtonsoft.Json;

namespace FlareQuotes.Infrastructure.Gmail;

/// <summary>
/// DPAPI-backed Google API token store.
/// 
/// Google's default FileDataStore writes OAuth token JSON to disk in plain text.
/// This store encrypts token payloads with Windows DPAPI CurrentUser scope and
/// opportunistically migrates old plain-text token files the first time they are read.
/// </summary>
public sealed class ProtectedFileDataStore : IDataStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Flare Fireplaces - Quotes v3 Gmail OAuth Token Store");
    private readonly string _folderPath;

    public ProtectedFileDataStore(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Token store folder path is required.", nameof(folderPath));

        _folderPath = folderPath;
        Directory.CreateDirectory(_folderPath);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var path = GetProtectedPath<T>(key);
        var json = JsonConvert.SerializeObject(value);
        var clearBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);

        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);

        if (File.Exists(path))
            File.Replace(tempPath, path, null);
        else
            File.Move(tempPath, path);

        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = GetProtectedPath<T>(key);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = GetProtectedPath<T>(key);

        if (File.Exists(path))
        {
            try
            {
                var protectedBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(clearBytes);
                return JsonConvert.DeserializeObject<T>(json)!;
            }
            catch
            {
                // A bad token file should not crash startup. Returning default lets the
                // Google OAuth flow request a fresh token.
                return default!;
            }
        }

        var migrated = await TryMigrateLegacyPlainTextTokenAsync<T>(key).ConfigureAwait(false);
        return migrated;
    }

    public Task ClearAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_folderPath, "*.dpapi", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(file); } catch { }
        }

        return Task.CompletedTask;
    }

    private async Task<T> TryMigrateLegacyPlainTextTokenAsync<T>(string key)
    {
        foreach (var legacyFile in Directory.EnumerateFiles(_folderPath, "*", SearchOption.TopDirectoryOnly)
                     .Where(x => !x.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)
                              && !x.EndsWith(".migrated", StringComparison.OrdinalIgnoreCase)
                              && !x.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var json = await File.ReadAllTextAsync(legacyFile).ConfigureAwait(false);

                if (!LooksLikeGoogleToken(json))
                    continue;

                var value = JsonConvert.DeserializeObject<T>(json);
                if (value is null)
                    continue;

                await StoreAsync(key, value).ConfigureAwait(false);

                try
                {
                    var migratedPath = legacyFile + ".migrated";
                    if (File.Exists(migratedPath))
                        File.Delete(migratedPath);

                    File.Move(legacyFile, migratedPath);
                }
                catch
                {
                    // Encryption succeeded. Failure to rename a legacy file should not
                    // block the user from continuing.
                }

                return value;
            }
            catch
            {
                // Ignore unrelated files in the token folder.
            }
        }

        return default!;
    }

    private static bool LooksLikeGoogleToken(string value)
    {
        return value.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("refresh_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("AccessToken", StringComparison.OrdinalIgnoreCase)
            || value.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase);
    }

    private string GetProtectedPath<T>(string key)
    {
        var normalized = $"{typeof(T).FullName}|{key}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_folderPath, $"google-token-{hash}.json.dpapi");
    }
}
