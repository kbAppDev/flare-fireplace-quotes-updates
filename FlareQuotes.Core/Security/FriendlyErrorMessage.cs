using System.Net.Mail;

namespace FlareQuotes.Core.Security;

public static class FriendlyErrorMessage
{
    public static string FromException(Exception exception, string fallback = "Something went wrong. Please try again.")
    {
        var message = exception.GetBaseException().Message;

        if (exception is FileNotFoundException || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "A required file could not be found. Check the app setup and try again.";

        if (exception is UnauthorizedAccessException)
            return "Windows blocked access to a required file or folder. Check permissions and try again.";

        if (exception is FormatException || exception is SmtpException || message.Contains("Invalid To header", StringComparison.OrdinalIgnoreCase))
            return "The email address or message format needs attention before a Gmail draft can be created.";

        if (message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "The app could not reach the required online service. Check the connection and try again.";
        }

        if (message.Contains("SHA", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("manifest", StringComparison.OrdinalIgnoreCase))
        {
            return "The update could not be verified, so it was not installed.";
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Something went wrong. Please try again." : fallback;
    }
}