using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IQuotePdfService
{
    Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath, CancellationToken cancellationToken = default);
}
