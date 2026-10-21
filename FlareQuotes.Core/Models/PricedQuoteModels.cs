namespace FlareQuotes.Core.Models;

public sealed class PricedQuoteResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public QuoteRequest Request { get; set; } = new();
    public List<PricedFireplaceQuote> Fireplaces { get; set; } = [];
    public IReadOnlyList<ResourceLinkSet> ResourceLinks { get; set; } = [];
    public int TotalFireplaceQuantity => Fireplaces.Sum(x => Math.Max(1, x.Quantity));
    public decimal TotalMsrp => Fireplaces.Sum(x => x.TotalMsrp);
    public bool HasDiscount => Fireplaces.Any(x => x.HasDiscount);
    public decimal TotalDiscount => Fireplaces.Sum(x => x.DiscountAmount);
    public decimal TotalAfterDiscount => Fireplaces.Sum(x => x.TotalAfterDiscount);
}

public sealed class PricedFireplaceQuote
{
    public string FireplaceLabel { get; set; } = string.Empty;
    public string FireplaceLocation { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectAddress { get; set; } = string.Empty;
    public FireplaceType Type { get; set; } = FireplaceType.Unknown;
    public string Model { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string GlassHeight { get; set; } = string.Empty;
    public string ModelNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string LeadTime { get; set; } = "3-5 Business Days";
    public decimal DiscountPercent { get; set; }
    // Extended fireplace MSRP only; included outdoor kits are deliberately excluded.
    public decimal DiscountEligibleMsrp { get; set; }
    public bool HasDiscount => DiscountPercent is > 0m and <= 100m &&
                               DiscountEligibleMsrp > 0m && BaseLine.Price.HasValue;
    public decimal DiscountAmount => HasDiscount
        ? decimal.Round(DiscountEligibleMsrp * DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero)
        : 0m;
    public decimal? DiscountedBasePrice => BaseLine.Price is { } price
        ? HasDiscount ? decimal.Round(price - DiscountAmount, 2, MidpointRounding.AwayFromZero) : price
        : null;
    // Match the displayed currency lines instead of rounding an unrounded option sum.
    public decimal TotalAfterDiscount => HasDiscount
        ? (DiscountedBasePrice ?? 0m) + OptionalFeatures.Sum(line =>
            decimal.Round(line.Price ?? 0m, 2, MidpointRounding.AwayFromZero))
        : TotalMsrp;
    public PriceLine BaseLine { get; set; } = new();
    public List<PriceLine> OptionalFeatures { get; set; } = [];
    public string ClassicMediaDisplay { get; set; } = string.Empty;
    public decimal TotalMsrp => (BaseLine.Price ?? 0m) + OptionalFeatures.Sum(x => x.Price ?? 0m);
}

public sealed class PriceLine
{
    public string Feature { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public int Quantity { get; set; } = 1;
    public string SourceSheet { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string PriceText => Price.HasValue ? Price.Value.ToString("C0") : string.Empty;
}
