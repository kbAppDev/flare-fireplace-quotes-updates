using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IPriceBookService
{
    Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request, CancellationToken cancellationToken = default);
    Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
                                               CancellationToken cancellationToken = default);
    Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
                                                  CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(QuoteRequest request, string pricingPath,
                                                                   CancellationToken cancellationToken = default);
}
