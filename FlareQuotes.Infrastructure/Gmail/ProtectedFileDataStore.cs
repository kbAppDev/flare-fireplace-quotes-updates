using System.Security.Cryptography;
using System.Text;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Security;
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
    private const long MaximumTokenBytes = 2L * 1024 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Flare Fireplace Quotes Gmail OAuth Token Store");
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
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var path = GetProtectedPath<T>(key);
        var json = JsonConvert.SerializeObject(value);
        var clearBytes = Encoding.UTF8.GetBytes(json);
        if (clearBytes.LongLength > MaximumTokenBytes)
            throw new InvalidDataException("Gmail token payload exceeds the maximum allowed size.");

        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }

        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = GetProtectedPath<T>(key);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = GetProtectedPath<T>(key);

        if (File.Exists(path))
        {
            try
            {
                return await ReadProtectedAsync<T>(path).ConfigureAwait(false);
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
        foreach (var file in Directory.EnumerateFiles(_folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (file.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
                else
                    SensitiveFileDeletion.DeletePlaintext(file);
            }
            catch
            {
            }
        }

        return Task.CompletedTask;
    }

    private async Task<T> TryMigrateLegacyPlainTextTokenAsync<T>(string key)
    {
        foreach (var legacyFile in LegacyTokenFiles().ToList())
        {
            try
            {
                if (new FileInfo(legacyFile).Length > MaximumTokenBytes)
                    continue;

                var json = await File.ReadAllTextAsync(legacyFile).ConfigureAwait(false);

                if (!LooksLikeGoogleToken(json))
                    continue;

                var value = JsonConvert.DeserializeObject<T>(json);
                if (value is null)
                    continue;

                await StoreAsync(key, value).ConfigureAwait(false);
                var verified = await ReadProtectedAsync<T>(GetProtectedPath<T>(key)).ConfigureAwait(false);
                if (verified is null)
                    throw new CryptographicException("Encrypted Gmail token verification failed after migration.");

                SensitiveFileDeletion.DeletePlaintext(legacyFile);

                return value;
            }
            catch
            {
                // Ignore unrelated files in the token folder.
            }
        }

        return default!;
    }

    private IEnumerable<string> LegacyTokenFiles()
    {
        var folders = new[] { _folderPath }
            .Concat(AppPaths.LegacyRoots.Select(root => Path.Combine(root, "gmail-token")))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                      .Where(path => !path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) &&
                                     !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<T> ReadProtectedAsync<T>(string path)
    {
        if (new FileInfo(path).Length > MaximumTokenBytes)
            throw new InvalidDataException("Protected Gmail token exceeds the maximum allowed size.");

        var protectedBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var json = Encoding.UTF8.GetString(clearBytes);
            return JsonConvert.DeserializeObject<T>(json)!;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static bool LooksLikeGoogleToken(string value)
    {
        return value.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AccessToken", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase);
    }

    private string GetProtectedPath<T>(string key)
    {
        var normalized = $"{typeof(T).FullName}|{key}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_folderPath, $"google-token-{hash}.json.dpapi");
    }
}
