using System.Text.RegularExpressions;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.Infrastructure.Logging;

public sealed class RedactingFileLogger : IAppLogger
{
    private const long MaximumLogBytes = 5L * 1024 * 1024;
    private static readonly TimeSpan ArchivedLogRetention = TimeSpan.FromDays(30);

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
    private static readonly Regex PhoneRegex =
        new(@"(?<!\d)(?:\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}(?!\d)",
            RegexOptions.Compiled);
    private static readonly Regex QuotePdfFilenameRegex =
        new(@"(?i)\bFlare Fireplaces? Quote[^\\/:*?""<>|\r\n]*\.pdf\b", RegexOptions.Compiled);
    private static readonly Regex QuotedWindowsPathRegex =
        new(@"(?i)[""'](?:[A-Z]:\\|\\\\)[^""'\r\n]+[""']", RegexOptions.Compiled);
    private static readonly Regex UnquotedWindowsPathRegex =
        new(@"(?i)(?:[A-Z]:\\|\\\\)[^\s""'\r\n]+", RegexOptions.Compiled);
    private static readonly Regex LocalUserPathRegex =
        new(@"(?i)(?:[A-Z]:|\\\\[^\\\r\n]+\\[^\\\r\n]+)\\Users\\[^\\\r\n]+",
            RegexOptions.Compiled);

    private readonly object _sync = new();

    public RedactingFileLogger()
    {
        LogFilePath = AppPaths.LogFile;
        DeleteExpiredArchive();
    }

    public string LogFilePath { get; }

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(Exception exception, string message) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var safeMessage = SingleLine(Redact(message));
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
        result = PhoneRegex.Replace(result, "[phone]");
        result = QuotePdfFilenameRegex.Replace(result, "[quote-pdf]");
        result = QuotedWindowsPathRegex.Replace(result, "[path]");
        result = UnquotedWindowsPathRegex.Replace(result, "[path]");
        result = RedactCurrentUserProfile(result);
        result = LocalUserPathRegex.Replace(result, "[user-profile]");
        return result;
    }

    private static string RedactCurrentUserProfile(string value)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
                   ? value
                   : value.Replace(userProfile, "[user-profile]", StringComparison.OrdinalIgnoreCase);
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < MaximumLogBytes)
            return;

        var archivedPath = Path.ChangeExtension(LogFilePath, ".previous.log");
        File.Move(LogFilePath, archivedPath, overwrite: true);
    }

    private void DeleteExpiredArchive()
    {
        try
        {
            var archivedPath = Path.ChangeExtension(LogFilePath, ".previous.log");
            var archived = new FileInfo(archivedPath);
            if (archived.Exists && DateTime.UtcNow - archived.LastWriteTimeUtc > ArchivedLogRetention)
                archived.Delete();
        }
        catch
        {
            // Log retention must never prevent application startup.
        }
    }
}
