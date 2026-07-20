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
    private const long MaximumPayloadBytes = 16L * 1024 * 1024;
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
                SensitiveFileDeletion.DeletePlaintext(legacyPlaintextPath);
            return existing;
        }

        if (string.IsNullOrWhiteSpace(legacyPlaintextPath) || !File.Exists(legacyPlaintextPath))
            return default;

        if (new FileInfo(legacyPlaintextPath).Length > MaximumPayloadBytes)
            throw new InvalidDataException("Legacy JSON payload exceeds the maximum allowed size.");

        var json = File.ReadAllText(legacyPlaintextPath, Encoding.UTF8);
        var value = JsonSerializer.Deserialize<T>(json, options);
        if (value is null)
            return default;

        Save(protectedPath, value, options);

        // Verify the encrypted replacement before removing the plaintext source.
        var verified = ReadProtected<T>(protectedPath, options);
        if (verified is null)
            throw new CryptographicException("Encrypted JSON verification failed after migration.");

        SensitiveFileDeletion.DeletePlaintext(legacyPlaintextPath);
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
        if (clearBytes.LongLength > MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            throw new InvalidDataException("JSON payload exceeds the maximum allowed size.");
        }

        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(clearBytes, _entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
        var payload = new byte[FileHeader.Length + protectedBytes.Length];
        Buffer.BlockCopy(FileHeader, 0, payload, 0, FileHeader.Length);
        Buffer.BlockCopy(protectedBytes, 0, payload, FileHeader.Length, protectedBytes.Length);

        var tempPath = protectedPath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, payload);

            if (File.Exists(protectedPath))
                File.Replace(tempPath, protectedPath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, protectedPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private T? ReadProtected<T>(string protectedPath, JsonSerializerOptions? options)
    {
        if (new FileInfo(protectedPath).Length > MaximumPayloadBytes + 4096)
            throw new InvalidDataException("Encrypted JSON payload exceeds the maximum allowed size.");

        var payload = File.ReadAllBytes(protectedPath);
        if (payload.Length <= FileHeader.Length || !payload.AsSpan(0, FileHeader.Length).SequenceEqual(FileHeader))
            throw new InvalidDataException("The encrypted JSON file header is invalid.");

        var protectedBytes = payload.AsSpan(FileHeader.Length).ToArray();
        var clearBytes = ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<T>(clearBytes, options);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

}
