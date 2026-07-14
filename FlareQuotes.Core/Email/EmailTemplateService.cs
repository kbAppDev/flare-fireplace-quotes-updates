using System.Net;
using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Email;

public sealed class EmailTemplateService
{
    private const string SectionSpacing = "<br><br><br>";

    public string BuildSubject(QuoteRequest request, PricedQuoteResult priced)
    {
        var firstFireplace = priced.Fireplaces.FirstOrDefault();
        var firstType = firstFireplace?.Type ?? FireplaceType.Indoor;
        var baseSubject = firstType switch {
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

        var consultation = WebUtility.HtmlEncode(settings.ConsultationUrl);
        var specLinks = BuildSpecLinks(resourceLinks);

        var fireplaceCount = priced.Fireplaces.Count;
        var firstSentence = fireplaceCount > 1 ? "Below are links to the product information with a quote for the " +
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

    private static string BuildSpecLinks(IReadOnlyList<ResourceLinkSet> sets)
    {
        if (sets.Count == 0)
            return "<strong>Spec Files:</strong> Resource links will be verified separately.";

        var lines = new List<string>();
        foreach (var set in sets)
        {
            var labelText = string.IsNullOrWhiteSpace(set.ModelNumber) ? "Spec Files" : $"{set.ModelNumber} Spec Files";

            var label = $"<strong>{WebUtility.HtmlEncode(labelText)}:</strong>";

            var links =
                set.Links.Where(x => !string.IsNullOrWhiteSpace(x.Value))
                    .Select(x => $"<a href=\"{WebUtility.HtmlEncode(x.Value)}\">{WebUtility.HtmlEncode(x.Key)}</a>");

            lines.Add(label + " " + string.Join(" | ", links));
        }

        return string.Join(SectionSpacing, lines);
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
