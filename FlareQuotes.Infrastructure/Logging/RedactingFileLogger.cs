using System.Text.RegularExpressions;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.Infrastructure.Logging;

public sealed class RedactingFileLogger : IAppLogger
{
    private const long MaximumLogBytes = 5L * 1024 * 1024;

    private static readonly Regex EmailRegex =
        new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TokenRegex =
        new(@"(?i)[""']?\b(access_token|refresh_token|client_secret)\b[""']?\s*[:=]\s*[""']?[^""'\s,}]+",
            RegexOptions.Compiled);
    private static readonly Regex AuthorizationRegex =
        new(@"(?i)[""']?\bauthorization\b[""']?\s*[:=]\s*[""']?bearer\s+[^""'\s,}]+",
            RegexOptions.Compiled);
    private static readonly Regex BearerRegex =
        new(@"(?i)\bbearer\s+[A-Z0-9._~+/=-]+", RegexOptions.Compiled);
    private static readonly Regex LocalUserPathRegex =
        new(@"C:\\Users\\[^\\\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _sync = new();

    public RedactingFileLogger()
    {
        LogFilePath = AppPaths.LogFile;
    }

    public string LogFilePath { get; }

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(Exception exception, string message) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var safeMessage = Redact(message);
            var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}";

            if (exception is not null)
                line += Environment.NewLine + Redact(exception.ToString());

            lock (_sync)
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    internal static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = EmailRegex.Replace(value, "[email]");
        result = TokenRegex.Replace(result, "$1=[redacted]");
        result = AuthorizationRegex.Replace(result, "authorization=Bearer [redacted]");
        result = BearerRegex.Replace(result, "Bearer [redacted]");
        result = LocalUserPathRegex.Replace(result, "C:\\Users\\[user]");
        return result;
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < MaximumLogBytes)
            return;

        var archivedPath = Path.ChangeExtension(LogFilePath, ".previous.log");
        File.Move(LogFilePath, archivedPath, overwrite: true);
    }
}
