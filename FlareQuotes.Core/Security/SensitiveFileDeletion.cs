namespace FlareQuotes.Core.Security;

/// <summary>
/// Best-effort removal for small plaintext files that may contain customer or OAuth data.
/// The overwrite reduces ordinary recovery risk but cannot guarantee erasure on SSDs or
/// copy-on-write storage, so callers must still avoid creating plaintext files when possible.
/// </summary>
public static class SensitiveFileDeletion
{
    private const long MaximumOverwriteBytes = 16L * 1024 * 1024;

    public static void DeletePlaintext(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length is > 0 and <= MaximumOverwriteBytes)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None,
                                                  bufferSize: 4096, FileOptions.WriteThrough);
                var zeros = new byte[Math.Min(81920, checked((int)info.Length))];
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
