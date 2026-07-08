namespace FlareQuotes.Core.Models;

public sealed class PriceRow
{
    public string SheetName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public Dictionary<string, string> RawValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PriceBookWorkbook
{
    public string SourcePath { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.Now;
    public List<string> SheetNames { get; set; } = [];
    public List<PriceRow> Rows { get; set; } = [];
}

public sealed class PriceBookMatch
{
    public bool Found { get; set; }
    public PriceRow? Row { get; set; }
    public string Reason { get; set; } = string.Empty;
}
