namespace FlareQuotes.Core.Models;

public sealed class QuoteRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Postal { get; set; } = string.Empty;
    public string ProjectAddress { get; set; } = string.Empty;
    public string InstallDate { get; set; } = string.Empty;
    public string FireplaceLocation { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string GlassHeight { get; set; } = string.Empty;
    public string QuoteDate { get; set; } = DateTime.Now.ToShortDateString();
    public string QuoteNumber { get; set; } = string.Empty;
    public string RawFeaturesText { get; set; } = string.Empty;
    public string RawRequestText { get; set; } = string.Empty;
    public List<FireplaceQuote> Fireplaces { get; set; } = [];
    public List<FeatureSelection> SelectedFeatures { get; set; } = [];
    public List<MediaSelection> ClassicMedia { get; set; } = [];
    public List<MediaSelection> PremiumMedia { get; set; } = [];
    public object? Tag { get; set; }
}
