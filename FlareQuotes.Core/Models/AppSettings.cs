namespace FlareQuotes.Core.Models;

public sealed class AppSettings
{
    public string SalesEmail { get; set; } = string.Empty;
    public string SalesPhone { get; set; } = string.Empty;
    public string Website { get; set; } = "https://flarefireplaces.com";
    public string HubSpotBcc { get; set; } = "2646235@bcc.na2.hubspot.com";
    public string ConsultationUrl { get; set; } = "https://meetings.hubspot.com/kyle533/jobsite-consultation";
    public string PricingFile { get; set; } = string.Empty;
    public string LastSaveDir { get; set; } = string.Empty;
    public bool UseGmailSignature { get; set; } = true;
    public string EmailSendMode { get; set; } = "draft";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = "https://github.com/kbAppDev/flare-fireplace-quotes-updates/" +
                                                    "releases/latest/download/flare-quotes-v1-latest.json";
    public string GmailCredentialsPath { get; set; } = string.Empty;
    public int RecallQuoteHistoryLimit { get; set; } = 5;
    public bool FirstRunHealthCheckCompleted { get; set; }
    public bool StrictManifestSignatureValidation { get; set; }
    public string UpdateManifestPublicKeyPem { get; set; } = string.Empty;
    public bool EnableRedactedLogs { get; set; } = true;
    public List<string> LeadTimePresets { get; set; } =
        ["3-5 Business Days", "1-2 Weeks", "2-4 Weeks", "4-6 Weeks", "6-8 Weeks", "8-10 Weeks", "10-12 Weeks", "TBD"];
}
