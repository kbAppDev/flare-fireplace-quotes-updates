namespace FlareQuotes.Core.Models;

public sealed record MediaOption(string Key, string Label, string DisplayName, string CalculationGroup, bool IsPremium,
                                 string[] Aliases);

public sealed class MediaSelection
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public decimal? Msrp { get; set; }
    public int Quantity { get; set; }
}
