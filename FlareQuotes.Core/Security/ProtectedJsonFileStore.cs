using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FlareQuotes.Core.Security;

/// <summary>
/// Stores JSON payloads encrypted with Windows DPAPI CurrentUser scope.
/// Supports one-time migration from a legacy plaintext JSON file.
/// </summary>
public sealed class ProtectedJsonFileStore
{
    private static readonly byte[] FileHeader = "FLARE-DPAPI-JSON-1\n"u8.ToArray();
    private readonly byte[] _entropy;

    public ProtectedJsonFileStore(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("A protection purpose is required.", nameof(purpose));

        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes("Flare Fireplace Quotes|" + purpose.Trim()));
    }

    public T? LoadOrMigrate<T>(string protectedPath, string? legacyPlaintextPath = null,
                               JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPath);

        if (File.Exists(protectedPath))
        {
            var existing = ReadProtected<T>(protectedPath, options);
            if (!string.IsNullOrWhiteSpace(legacyPlaintextPath) && File.Exists(legacyPlaintextPath))
                SecureDeletePlaintext(legacyPlaintextPath);
            return existing;
        }

        if (string.IsNullOrWhiteSpace(legacyPlaintextPath) || !File.Exists(legacyPlaintextPath))
            return default;

        var json = File.ReadAllText(legacyPlaintextPath, Encoding.UTF8);
        var value = JsonSerializer.Deserialize<T>(json, options);
        if (value is null)
            return default;

        Save(protectedPath, value, options);

        // Verify the encrypted replacement before removing the plaintext source.
        var verified = ReadProtected<T>(protectedPath, options);
        if (verified is null)
            throw new CryptographicException("Encrypted JSON verification failed after migration.");

        SecureDeletePlaintext(legacyPlaintextPath);
        return value;
    }

    public void Save<T>(string protectedPath, T value, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPath);
        ArgumentNullException.ThrowIfNull(value);

        var directory = Path.GetDirectoryName(protectedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var protectedBytes = ProtectedData.Protect(clearBytes, _entropy, DataProtectionScope.CurrentUser);
        var payload = new byte[FileHeader.Length + protectedBytes.Length];
        Buffer.BlockCopy(FileHeader, 0, payload, 0, FileHeader.Length);
        Buffer.BlockCopy(protectedBytes, 0, payload, FileHeader.Length, protectedBytes.Length);

        var tempPath = protectedPath + ".tmp";
        File.WriteAllBytes(tempPath, payload);

        if (File.Exists(protectedPath))
            File.Replace(tempPath, protectedPath, null, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, protectedPath);
    }

    private T? ReadProtected<T>(string protectedPath, JsonSerializerOptions? options)
    {
        var payload = File.ReadAllBytes(protectedPath);
        if (payload.Length <= FileHeader.Length || !payload.AsSpan(0, FileHeader.Length).SequenceEqual(FileHeader))
            throw new InvalidDataException("The encrypted JSON file header is invalid.");

        var protectedBytes = payload.AsSpan(FileHeader.Length).ToArray();
        var clearBytes = ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<T>(clearBytes, options);
    }

    private static void SecureDeletePlaintext(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 0 && info.Length <= 16 * 1024 * 1024)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None,
                                                  bufferSize: 4096, FileOptions.WriteThrough);
                var zeros = new byte[Math.Min(81920, (int)Math.Min(info.Length, int.MaxValue))];
                long remaining = info.Length;

                while (remaining > 0)
                {
                    var count = (int)Math.Min(zeros.Length, remaining);
                    stream.Write(zeros, 0, count);
                    remaining -= count;
                }

                stream.Flush(flushToDisk: true);
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
