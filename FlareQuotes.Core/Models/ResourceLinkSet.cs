namespace FlareQuotes.Core.Models;

public sealed class ResourceLinkSet
{
    public string ModelNumber { get; set; } = string.Empty;
    public Dictionary<string, string> Links { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
