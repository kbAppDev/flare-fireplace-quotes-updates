using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace FlareQuotes.Infrastructure.Gmail;

public sealed class GmailDraftService : IGmailDraftService
{
    private const int MaxAttachmentBytes = 20 * 1024 * 1024;

    private static readonly string[] Scopes =
    [
        GmailService.Scope.GmailCompose,
        GmailService.Scope.GmailSettingsBasic
    ];

    private readonly ISettingsService _settingsService;
    private GmailService? _service;

    public GmailDraftService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken).ConfigureAwait(false);
        var profile = await service.Users.GetProfile("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return profile.EmailAddress ?? "Connected";
    }

    public async Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken).ConfigureAwait(false);
        var profile = await service.Users.GetProfile("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return profile.EmailAddress ?? "Connected";
    }

    public async Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetServiceAsync(cancellationToken).ConfigureAwait(false);
            var list = await service.Users.Settings.SendAs.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var sender = list.SendAs?.FirstOrDefault(x => x.IsDefault == true) ?? list.SendAs?.FirstOrDefault();
            return sender?.Signature ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetServiceAsync(cancellationToken).ConfigureAwait(false);
            var raw = BuildRawMessage(request);
            var draft = new Draft { Message = new Message { Raw = raw } };
            var created = await service.Users.Drafts.Create(draft, "me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            OpenGmailDrafts();

            return new EmailDraftResult
            {
                Success = true,
                DraftId = created.Id ?? string.Empty,
                OpenedGmail = true,
                Message = "Gmail draft created."
            };
        }
        catch (Exception ex)
        {
            return new EmailDraftResult
            {
                Success = false,
                OpenedGmail = false,
                Message = $"Gmail draft could not be created. {SafeForUser(ex.Message)}"
            };
        }
    }

    private async Task<GmailService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null)
            return _service;

        var credentialPath = await FindCredentialsPathAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(credentialPath))
            throw new FileNotFoundException("gmail_credentials.json was not found.", credentialPath);

        await using var stream = File.OpenRead(credentialPath);

        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flare Fireplaces - Quotes",
            "v3",
            "gmail-token");

        Directory.CreateDirectory(tokenDir);

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            Scopes,
            "user",
            cancellationToken,
            new ProtectedFileDataStore(tokenDir)).ConfigureAwait(false);

        _service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Flare Fireplaces - Quotes"
        });

        return _service;
    }

    private static string BuildRawMessage(EmailDraftRequest request)
    {
        var toHeader = BuildAddressHeader(request.ToEmail, required: true);
        var bccHeader = BuildAddressHeader(request.BccEmail, required: false);
        var subjectHeader = EncodeHeader(request.Subject);
        var boundary = "----=_FlareQuotes_" + Guid.NewGuid().ToString("N");

        var sb = new StringBuilder();
        sb.AppendLine($"To: {toHeader}");
        if (!string.IsNullOrWhiteSpace(bccHeader))
            sb.AppendLine($"Bcc: {bccHeader}");

        sb.AppendLine($"Subject: {subjectHeader}");
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        sb.AppendLine();

        sb.AppendLine($"--{boundary}");
        sb.AppendLine("Content-Type: text/html; charset=UTF-8");
        sb.AppendLine("Content-Transfer-Encoding: base64");
        sb.AppendLine();
        sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.HtmlBody ?? string.Empty), Base64FormattingOptions.InsertLineBreaks));
        AppendQuoteAttachment(sb, boundary, request.PdfAttachmentPath, requirePdf: true);

        foreach (var attachmentPath in request.AdditionalAttachmentPaths ?? [])
        {
            AppendQuoteAttachment(sb, boundary, attachmentPath, requirePdf: false);
        }

        sb.AppendLine($"--{boundary}--");
        return Base64UrlEncode(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string BuildAddressHeader(string value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                throw new InvalidOperationException("A valid recipient email is required.");

            return string.Empty;
        }

        var addresses = value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeHeaderValue)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (addresses.Count == 0)
        {
            if (required)
                throw new InvalidOperationException("A valid recipient email is required.");

            return string.Empty;
        }

        var valid = new List<string>();
        foreach (var address in addresses)
        {
            if (!MailAddress.TryCreate(address, out var parsed))
                throw new InvalidOperationException("One or more email addresses are invalid.");

            valid.Add(parsed.Address);
        }

        return string.Join(", ", valid);
    }

    private static string EncodeHeader(string value)
    {
        var sanitized = SanitizeHeaderValue(value);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Flare Fireplaces Quote";

        return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(sanitized)) + "?=";
    }

    private static string SanitizeHeaderValue(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static void AppendQuoteAttachment(StringBuilder sb, string boundary, string attachmentPath, bool requirePdf)
    {
        if (string.IsNullOrWhiteSpace(attachmentPath) || !File.Exists(attachmentPath))
            return;

        var fileInfo = new FileInfo(attachmentPath);
        var extension = fileInfo.Extension.ToLowerInvariant();

        if (requirePdf && !string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only PDF quote attachments are supported for the quote file.");

        if (!requirePdf && !IsSupportedImageAttachment(extension))
            return;

        if (fileInfo.Length > MaxAttachmentBytes)
            throw new InvalidOperationException($"{fileInfo.Name} is too large to attach.");

        var filename = SanitizeAttachmentFileName(fileInfo.Name);
        var contentType = requirePdf ? "application/pdf" : GetImageContentType(extension);
        var bytes = File.ReadAllBytes(fileInfo.FullName);

        sb.AppendLine($"--{boundary}");
        sb.AppendLine($"Content-Type: {contentType}; name=\"{filename}\"");
        sb.AppendLine("Content-Transfer-Encoding: base64");
        sb.AppendLine($"Content-Disposition: attachment; filename=\"{filename}\"");
        sb.AppendLine();
        AppendFlareBase64AttachmentLines(sb, bytes);
        sb.AppendLine();
    }

    private static void AppendFlareBase64AttachmentLines(StringBuilder sb, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);

        for (var i = 0; i < base64.Length; i += 76)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(76, base64.Length - i)));
        }
    }

    private static bool IsSupportedImageAttachment(string extension)
    {
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static string GetImageContentType(string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static string SanitizeAttachmentFileName(string value)
    {
        var cleaned = SanitizeHeaderValue(value);
        foreach (var c in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(c, '_');

        return string.IsNullOrWhiteSpace(cleaned) ? "FlareQuote.pdf" : cleaned;
    }

    private static string SafeForUser(string value)
    {
        var sanitized = SanitizeHeaderValue(value);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(local))
            sanitized = sanitized.Replace(local, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static void OpenGmailDrafts()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://mail.google.com/mail/u/0/#drafts")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser launch failure should not prevent draft creation.
        }
    }

    private async Task<string> FindCredentialsPathAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.GmailCredentialsPath))
            candidates.Add(settings.GmailCredentialsPath);

        candidates.AddRange([
            Path.Combine(Environment.CurrentDirectory, "LocalData", "gmail_credentials.json"),
            Path.Combine(AppContext.BaseDirectory, "LocalData", "gmail_credentials.json"),
            Path.Combine(Environment.CurrentDirectory, "gmail_credentials.json"),
            Path.Combine(AppContext.BaseDirectory, "gmail_credentials.json")
        ]);

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var dir = baseDir; dir is not null; dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "LocalData", "gmail_credentials.json"));
            candidates.Add(Path.Combine(dir.FullName, "gmail_credentials.json"));

            if (File.Exists(Path.Combine(dir.FullName, "FlareQuotes.App", "FlareQuotes.App.csproj")))
                break;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}


