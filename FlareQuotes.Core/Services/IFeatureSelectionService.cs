using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Services;

public interface IFeatureSelectionService
{
    IReadOnlyList<FeatureOption> GetAvailableOptions(FireplaceType type);
    IReadOnlyList<FeatureOption> DetectFromText(string rawText, FireplaceType type);
}
