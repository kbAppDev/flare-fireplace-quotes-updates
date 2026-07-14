using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace FlareQuotes.Infrastructure.Gmail;

public sealed class GmailDraftService : IGmailDraftService
{
    private const int MaxAttachmentBytes = 20 * 1024 * 1024;
    // Keep the binary payload comfortably below Gmail's encoded message limit.
    // Base64 expands attachments by roughly one third, and MIME headers add more.
    private const long MaxTotalAttachmentBytes = 18L * 1024 * 1024;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(45);

    private static readonly string[] Scopes = [GmailService.Scope.GmailCompose, GmailService.Scope.GmailSettingsBasic];

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private GmailService? _service;

    public GmailDraftService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);
        var service = await GetServiceAsync(timeout.Token).ConfigureAwait(false);
        var profile = await service.Users.GetProfile("me").ExecuteAsync(timeout.Token).ConfigureAwait(false);
        return profile.EmailAddress ?? "Connected";
    }

    public async Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);
        var service = await GetServiceAsync(timeout.Token).ConfigureAwait(false);
        var profile = await service.Users.GetProfile("me").ExecuteAsync(timeout.Token).ConfigureAwait(false);
        return profile.EmailAddress ?? "Connected";
    }

    public async Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ApiTimeout);
            var service = await GetServiceAsync(timeout.Token).ConfigureAwait(false);
            var list = await service.Users.Settings.SendAs.List("me").ExecuteAsync(timeout.Token).ConfigureAwait(false);
            var sender = list.SendAs?.FirstOrDefault(x => x.IsDefault == true) ?? list.SendAs?.FirstOrDefault();
            return sender?.Signature ?? string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Gmail signature lookup was skipped. Reason={SafeForUser(ex.Message)}");
            return string.Empty;
        }
    }

    public async Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request,
                                                         CancellationToken cancellationToken = default)
    {
        try
        {
            var pdfBytes = File.Exists(request.PdfAttachmentPath) ? new FileInfo(request.PdfAttachmentPath).Length : 0;
            var manualBytes =
                (request.AdditionalAttachmentPaths ?? []).Where(File.Exists).Sum(path => new FileInfo(path).Length);

            _logger.Info(
                $"Gmail service preparing request. SubjectLength={request.Subject?.Length ?? 0}; HtmlLength={request.HtmlBody?.Length ?? 0}; PdfBytes={pdfBytes}; ManualAttachmentCount={request.AdditionalAttachmentPaths?.Count ?? 0}; ManualBytes={manualBytes}.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ApiTimeout);
            var service = await GetServiceAsync(timeout.Token).ConfigureAwait(false);
            _logger.Info("Gmail service authenticated. Building MIME message.");

            var raw = BuildRawMessage(request);
            _logger.Info($"Gmail MIME message built. EncodedCharacters={raw.Length}.");

            var draft = new Draft { Message = new Message { Raw = raw } };
            var created =
                await service.Users.Drafts.Create(draft, "me").ExecuteAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(created.Id))
            {
                _logger.Warning("Gmail API returned without a draft ID.");
                return new EmailDraftResult { Success = false, OpenedGmail = false,
                                              Message = "Gmail did not confirm the draft was created." };
            }

            var opened = request.OpenBrowserAfterCreate && OpenGmailDrafts();
            _logger.Info("Gmail API created draft. DraftIdPresent=True; " + $"BrowserOpened={opened}.");

            return new EmailDraftResult {
                Success = true, DraftId = created.Id, OpenedGmail = opened,
                Message = request.OpenBrowserAfterCreate
                              ? (opened ? "Gmail draft created."
                                        : "Gmail draft created, but the browser could not be opened automatically.")
                              : "Gmail draft created."
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning("Gmail draft creation timed out.");
            return new EmailDraftResult { Success = false,
                                          Message =
                                              "Gmail draft creation timed out. Check the connection and try again." };
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.Error(ex,
                          $"Gmail API rejected draft. HttpStatus={(int)ex.HttpStatusCode}; Service={ex.ServiceName}.");
            return new EmailDraftResult {
                Success = false, OpenedGmail = false,
                Message = $"Gmail rejected the draft (HTTP {(int)ex.HttpStatusCode}). {SafeForUser(ex.Message)}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Gmail draft service failed before confirmation.");
            return new EmailDraftResult { Success = false, OpenedGmail = false,
                                          Message = $"Gmail draft could not be created. {SafeForUser(ex.Message)}" };
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

        AppPaths.MigrateLegacyData();
        var tokenDir = AppPaths.GmailTokenStore;

        var credential = await GoogleWebAuthorizationBroker
                             .AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets, Scopes, "user",
                                             cancellationToken, new ProtectedFileDataStore(tokenDir))
                             .ConfigureAwait(false);

        _service = new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential,
                                                                        ApplicationName = AppPaths.ProductFolderName });

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
        sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(request.HtmlBody ?? string.Empty),
                                             Base64FormattingOptions.InsertLineBreaks));
        long attachedBytes = 0;
        attachedBytes +=
            AppendQuoteAttachment(sb, boundary, request.PdfAttachmentPath, requirePdf: true, MaxTotalAttachmentBytes);

        foreach (var attachmentPath in request.AdditionalAttachmentPaths ?? [])
        {
            var remainingBytes = MaxTotalAttachmentBytes - attachedBytes;
            if (remainingBytes <= 0)
                break;

            attachedBytes += AppendQuoteAttachment(sb, boundary, attachmentPath, requirePdf: false, remainingBytes);
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

        var addresses = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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
        return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static long AppendQuoteAttachment(StringBuilder sb, string boundary, string attachmentPath, bool requirePdf,
                                              long remainingBytes)
    {
        if (string.IsNullOrWhiteSpace(attachmentPath) || !File.Exists(attachmentPath))
            return 0;

        var fileInfo = new FileInfo(attachmentPath);
        var extension = fileInfo.Extension.ToLowerInvariant();

        if (requirePdf && !string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only PDF quote attachments are supported for the quote file.");

        if (!requirePdf && !IsSupportedImageAttachment(extension))
            return 0;

        if (fileInfo.Length > MaxAttachmentBytes)
        {
            if (requirePdf)
                throw new InvalidOperationException($"{fileInfo.Name} is too large to attach.");

            return 0;
        }

        if (fileInfo.Length > remainingBytes)
        {
            if (requirePdf)
                throw new InvalidOperationException("The quote PDF is too large to create a Gmail draft.");

            return 0;
        }

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
        return fileInfo.Length;
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
        return extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
                                  _ => "application/octet-stream" };
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
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool OpenGmailDrafts()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://mail.google.com/mail/u/0/#drafts") { UseShellExecute = true });
            return true;
        }
        catch
        {
            // Browser launch failure should not prevent draft creation.
            return false;
        }
    }

    private async Task<string> FindCredentialsPathAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        AppPaths.ImportGmailCredentials(settings.GmailCredentialsPath);
        return AppPaths.GmailCredentialsFile;
    }

    public async Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("Draft ID is required.", nameof(draftId));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);
        var service = await GetServiceAsync(timeout.Token).ConfigureAwait(false);
        await service.Users.Drafts.Delete("me", draftId).ExecuteAsync(timeout.Token).ConfigureAwait(false);
        _logger.Info("Gmail integration-test draft deleted successfully.");
    }
}
