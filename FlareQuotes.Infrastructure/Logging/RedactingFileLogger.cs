using System.Text.RegularExpressions;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.Infrastructure.Logging;

public sealed class RedactingFileLogger : IAppLogger
{
    private static readonly Regex EmailRegex =
        new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TokenRegex =
        new(@"(?i)(access_token|refresh_token|client_secret|authorization|bearer)\s*[:=]\s*[""']?[^""'\s,}]+",
            RegexOptions.Compiled);
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
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = EmailRegex.Replace(value, "[email]");
        result = TokenRegex.Replace(result, "$1=[redacted]");
        result = LocalUserPathRegex.Replace(result, "C:\\Users\\[user]");
        return result;
    }
}