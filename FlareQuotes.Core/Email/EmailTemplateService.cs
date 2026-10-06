using System.Net;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Security;

namespace FlareQuotes.Core.Email;

public sealed class EmailTemplateService
{
    private const string SectionSpacing = "<br><br><br>";
    private const string IndoorConsultationUrl = "https://meetings.hubspot.com/kyle533/jobsite-consultation";

    public string BuildSubject(QuoteRequest request, PricedQuoteResult priced)
    {
        var firstFireplace = priced.Fireplaces.FirstOrDefault();
        var firstType = firstFireplace?.Type ?? FireplaceType.Indoor;
        var baseSubject = firstType switch
        {
            FireplaceType.IndoorOutdoorSeeThrough => "Your Flare Indoor-Outdoor See Through Fireplace Quote",
            FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough => "Your Flare Outdoor Fireplace Quote",
            FireplaceType.Large => "Your Large Flare Fireplace Quote",
            FireplaceType.Traditional => "Your Flare Fireplaces Traditional Fireplace Information",
            _ => "Your Flare Fireplaces Indoor Fireplace Information"
        };

        var modelSuffix = BuildSubjectModelSuffix(firstFireplace);
        return string.IsNullOrWhiteSpace(modelSuffix) ? baseSubject : $"{baseSubject} | {modelSuffix}";
    }

    public string BuildHtml(QuoteRequest request, PricedQuoteResult priced,
                            IReadOnlyList<ResourceLinkSet> resourceLinks, AppSettings settings, string signatureHtml)
    {
        var firstName = FirstName(request.ClientName);
        var greeting = string.IsNullOrWhiteSpace(firstName)
                           ? "Hello,"
                           : $"<strong><em>{WebUtility.HtmlEncode(firstName)},</em></strong>";

        var isIndoorQuote = IsIndoorQuote(request, priced);
        var consultationUrl = isIndoorQuote
                                  ? IndoorConsultationUrl
                                  : TrustedExternalLinkPolicy.TryNormalizeConsultation(
                                        settings.ConsultationUrl, out var trustedConsultation)
                                      ? trustedConsultation
                                      : "https://flarefireplaces.com/";
        var consultation = WebUtility.HtmlEncode(consultationUrl);
        var specLinks = BuildSpecLinks(resourceLinks);

        var fireplaceCount = priced.TotalFireplaceQuantity;
        var firstSentence = isIndoorQuote
                                ? "Below are links to the product information, including a quote for the " +
                                      "fireplace and its optional features."
                                : fireplaceCount > 1
                                    ? "Below are links to the product information with a quote for the " +
                                          "fireplace(s) and their optional features."
                                    : "Below are links to the product information with a quote for the " +
                                          "fireplace and its optional features.";

        var firstParagraph = firstSentence + (" The listed prices are the Manufacturer's Suggested Retail Price " +
                                              "(MSRP), valid for 30 days, and do not include installation costs.");

        var secondParagraph =
            $"If you have any questions or are ready to proceed, please use the information in my email signature to reach me directly and schedule a <a href=\"{consultation}\">project consultation</a>.";

        var helpLine =
            $"<strong>Need More Help?</strong><br>Schedule an <a href=\"{consultation}\">Online Consultation</a>";

        var body =
            string.Join(SectionSpacing, greeting + "<br>" + firstParagraph, secondParagraph, specLinks, helpLine,
                        "Looking forward to helping you create a warm and inviting space with Flare Fireplaces! 🔥");

        if (!string.IsNullOrWhiteSpace(signatureHtml))
            body += SectionSpacing + signatureHtml;

        return body;
    }

    private static bool IsIndoorQuote(QuoteRequest request, PricedQuoteResult priced)
    {
        var types = priced.Fireplaces.Count > 0
                        ? priced.Fireplaces.Select(fireplace => fireplace.Type)
                        : request.Fireplaces.Select(fireplace => fireplace.Type);

        return types.Any() && types.All(type => type is FireplaceType.Indoor or FireplaceType.IndoorSeeThrough or
                                                   FireplaceType.Traditional or FireplaceType.Large);
    }

    private static string BuildSpecLinks(IReadOnlyList<ResourceLinkSet> sets)
    {
        if (sets.Count == 0)
            return "<strong>Spec Files:</strong> Resource links will be verified separately.";

        var lines = new List<string>();
        foreach (var set in sets)
        {
            var location = (set.FireplaceLocation ?? string.Empty).Trim();
            var modelNumber = (set.ModelNumber ?? string.Empty).Trim();
            var labelText = !string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(modelNumber)
                                ? $"{location} — {modelNumber} Spec Files"
                                : !string.IsNullOrWhiteSpace(location)
                                    ? $"{location} Spec Files"
                                    : !string.IsNullOrWhiteSpace(modelNumber)
                                        ? $"{modelNumber} Spec Files"
                                        : "Spec Files";

            var label = $"<strong>{WebUtility.HtmlEncode(labelText)}:</strong>";

            var links = set.Links.Select(x =>
                                TrustedExternalLinkPolicy.TryNormalize(x.Value, out var url)
                                    ? new KeyValuePair<string, string>(x.Key, url)
                                    : default)
                           .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                           .Select(x => $"<a href=\"{WebUtility.HtmlEncode(x.Value)}\">{WebUtility.HtmlEncode(x.Key)}</a>")
                           .ToArray();

            if (links.Length > 0)
                lines.Add(label + " " + string.Join(" | ", links));
        }

        return lines.Count == 0
                   ? "<strong>Spec Files:</strong> No verified resource links are available."
                   : string.Join(SectionSpacing, lines);
    }

    private static string BuildSubjectModelSuffix(PricedFireplaceQuote? fireplace)
    {
        if (fireplace is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(fireplace.ModelNumber))
            return fireplace.ModelNumber.Trim();

        if (!string.IsNullOrWhiteSpace(fireplace.Description))
            return fireplace.Description.Trim();

        if (!string.IsNullOrWhiteSpace(fireplace.FireplaceLabel))
            return fireplace.FireplaceLabel.Trim();

        return string.Empty;
    }

    private static string FirstName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }
}
