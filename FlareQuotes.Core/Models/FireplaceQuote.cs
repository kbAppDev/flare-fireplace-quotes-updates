namespace FlareQuotes.Core.Models;

public sealed class FireplaceQuote
{
    public string FireplaceLocation { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectAddress { get; set; } = string.Empty;
    public FireplaceType Type { get; set; } = FireplaceType.Unknown;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string GlassHeight { get; set; } = string.Empty;
    public string ModelNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? BaseMsrp { get; set; }
    public string LeadTime { get; set; } = "3-5 Business Days";
    public string ClassicMediaDisplay { get; set; } = string.Empty;
    public List<FeatureSelection> Features { get; set; } = [];
    public List<MediaSelection> PremiumMedia { get; set; } = [];
}

