using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IGmailDraftService
{
    Task<string> ConnectAsync(CancellationToken cancellationToken = default);
    Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default);
    Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default);
    Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request, CancellationToken cancellationToken = default);
}
