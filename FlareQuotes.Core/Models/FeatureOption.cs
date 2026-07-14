namespace FlareQuotes.Core.Models;

public sealed record FeatureOption(string Key, string DisplayName, string PdfDescription, FireplaceType[] AppliesTo,
                                   bool RequiresSize, string PreferredSheet, string[] Aliases);

public sealed class FeatureSelection
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PdfDescription { get; set; } = string.Empty;
    public decimal? Msrp { get; set; }
    public string SourceSku { get; set; } = string.Empty;
}
