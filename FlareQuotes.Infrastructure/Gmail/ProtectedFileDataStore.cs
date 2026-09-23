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
    private readonly IReadOnlyList<string> _legacyTokenFolders;
    private readonly IReadOnlyList<string> _obsoleteTokenFolders;
    private readonly bool _allowLegacyMigration;

    public ProtectedFileDataStore(string folderPath, bool allowLegacyMigration = true) :
        this(folderPath, AppPaths.LegacyGmailTokenStores, [AppPaths.ObsoleteGoogleTokenCertBuilderStore],
             allowLegacyMigration)
    {
    }

    internal ProtectedFileDataStore(string folderPath, IEnumerable<string> legacyTokenFolders,
                                    IEnumerable<string> obsoleteTokenFolders, bool allowLegacyMigration = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Token store folder path is required.", nameof(folderPath));

        _folderPath = Path.GetFullPath(folderPath);
        _legacyTokenFolders = legacyTokenFolders.Select(Path.GetFullPath)
            .Where(path => !string.Equals(path, _folderPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _obsoleteTokenFolders = obsoleteTokenFolders.Select(Path.GetFullPath)
            .Where(path => !string.Equals(path, _folderPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _allowLegacyMigration = allowLegacyMigration;
        Directory.CreateDirectory(_folderPath);
    }

    public async Task StoreAsync<T>(string key, T value)
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

        var verified = await ReadProtectedAsync<T>(path).ConfigureAwait(false);
        if (verified is null)
            throw new CryptographicException("Encrypted Gmail token verification failed after storage.");

        CleanupLegacyPlaintextTokens<T>(key);
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
                var value = await ReadProtectedAsync<T>(path).ConfigureAwait(false);
                if (value is not null)
                    CleanupLegacyPlaintextTokens<T>(key);

                return value;
            }
            catch
            {
                // A bad token file should not crash startup. Returning default lets the
                // Google OAuth flow request a fresh token.
                return default!;
            }
        }

        return _allowLegacyMigration
                   ? await TryMigrateLegacyPlainTextTokenAsync<T>(key).ConfigureAwait(false)
                   : default!;
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
        foreach (var legacyFile in LegacyTokenFiles<T>(key).ToList())
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

                return value;
            }
            catch
            {
                // Ignore unrelated files in the token folder.
            }
        }

        return default!;
    }

    private IEnumerable<string> LegacyTokenFiles<T>(string key)
    {
        var legacyFileName = $"{typeof(T).FullName}-{key}";
        var folders = new[] { _folderPath }.Concat(_legacyTokenFolders)
            .Where(IsRegularDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return folders.Select(folder => Path.Combine(folder, legacyFileName)).Where(IsRegularBoundedFile);
    }

    private void CleanupLegacyPlaintextTokens<T>(string key)
    {
        foreach (var path in LegacyTokenFiles<T>(key).Concat(ObsoleteTokenFiles()).Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                SensitiveFileDeletion.DeletePlaintext(path);
            }
            catch
            {
                // A verified protected token remains authoritative even when legacy cleanup is denied.
            }
        }

        foreach (var folder in _legacyTokenFolders.Concat(_obsoleteTokenFolders).Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            TryDeleteEmptyRegularDirectory(folder);
        }
    }

    private IEnumerable<string> ObsoleteTokenFiles()
    {
        foreach (var folder in _obsoleteTokenFolders.Where(IsRegularDirectory))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var path in files.Where(IsRegularBoundedFile))
            {
                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                if (LooksLikeGoogleToken(text))
                    yield return path;
            }
        }
    }

    private static bool IsRegularDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRegularBoundedFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                   info.Length is > 0 and <= MaximumTokenBytes;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteEmptyRegularDirectory(string path)
    {
        try
        {
            if (IsRegularDirectory(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path, recursive: false);
        }
        catch
        {
        }
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
