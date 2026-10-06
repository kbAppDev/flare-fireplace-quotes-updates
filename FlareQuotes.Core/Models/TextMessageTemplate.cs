namespace FlareQuotes.Core.Models;

public sealed class TextMessageTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class TextMessageTemplateLibrary
{
    public List<TextMessageTemplate> Templates { get; set; } = [];
    public string SalesName { get; set; } = string.Empty;
    public string SelectedTemplateId { get; set; } = string.Empty;
}

public sealed record TextMessageContext
{
    public string ClientName { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string ConsultationUrl { get; init; } = string.Empty;
    public string SalesName { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = "Current quote";
}

public sealed record TextMessageRenderResult(string Text, IReadOnlyList<string> UnknownTokens,
                                             IReadOnlyList<string> MissingValues);
