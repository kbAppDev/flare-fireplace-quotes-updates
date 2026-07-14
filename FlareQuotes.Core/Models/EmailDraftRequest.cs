namespace FlareQuotes.Core.Models;

public sealed class EmailDraftRequest
{
    public string ToEmail { get; set; } = string.Empty;
    public string BccEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string PdfAttachmentPath { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> AdditionalAttachmentPaths { get; set; } = [];
    public bool OpenBrowserAfterCreate { get; set; } = true;
}

public sealed class EmailDraftResult
{
    public bool Success { get; set; }
    public string DraftId { get; set; } = string.Empty;
    public bool OpenedGmail { get; set; }
    public string Message { get; set; } = string.Empty;
}
