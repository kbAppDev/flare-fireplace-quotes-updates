using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IMediaSelectionService
{
    IReadOnlyList<MediaOption> GetClassicMedia(FireplaceType type);
    IReadOnlyList<MediaOption> GetPremiumMedia(FireplaceType type);
    IReadOnlyList<MediaOption> DetectFromText(string rawText, FireplaceType type);
}
