using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Settings;
using FlareQuotes.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FlareQuotes.Infrastructure.Pdf;

public sealed class QuestPdfQuotePdfService : IQuotePdfService
{
    private const string FeatureBlue = "#6F98A0";
    private const string TextDark = "#111111";
    private const string TextMuted = "#555555";
    private const string RuleGray = "#7A7A7A";
    private const string FlareRed = "#E74B4B";

    public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
                                           CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = LicenseType.Community;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

        if (request.Tag is not PricedQuoteResult priced)
            throw new InvalidOperationException(
                "QuoteRequest.Tag must contain PricedQuoteResult before PDF generation.");

        var logo = FindLogoPath();
        var quoteDate = DisplayDateShort(request.QuoteDate);
        var quoteNumber = FirstNonBlank(request.QuoteNumber, "0001");
        var fireplaces = priced.Fireplaces;

        Document
            .Create(container =>
                    {
                        if (fireplaces.Count == 0)
                        {
                            container.Page(page =>
                                               RenderFireplacePage(page, request, null, logo, quoteDate, quoteNumber));
                            return;
                        }

                        foreach (var fireplace in fireplaces)
                        {
                            container.Page(
                                page => RenderFireplacePage(page, request, fireplace, logo, quoteDate, quoteNumber));
                        }
                    })
            .GeneratePdf(outputPath);

        return Task.FromResult(outputPath);
    }

    private static void RenderFireplacePage(PageDescriptor page, QuoteRequest request, PricedFireplaceQuote? fireplace,
                                            string logo, string quoteDate, string quoteNumber)
    {
        // PDF hyperlink resource context
        var pricedContext = request.Tag as PricedQuoteResult;
        var resourceLinkSet =
            fireplace is null ? null : FindMatchingResourceLinkSet(pricedContext?.ResourceLinks, fireplace);
        page.Size(PageSizes.Letter);
        page.MarginTop(58);
        page.MarginBottom(52);
        page.MarginLeft(74);
        page.MarginRight(74);
        page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(7.2f).FontColor(TextDark));

        page.Content().Column(
            col =>
            {
                col.Spacing(8);

                col.Item().Element(c => HeaderBlock(c, logo));

                col.Item().PaddingTop(3).Text("Customer & Project Details").FontSize(8.2f).Bold();
                col.Item().Element(c => CustomerProjectDetails(c, request, quoteDate, quoteNumber, fireplace));

                col.Item().PaddingTop(8).Text("Pricing Breakdown").FontSize(8.2f).Bold();
                col.Item().Element(
                    c => PricingTable(c, fireplace is null ? Array.Empty<PricedFireplaceQuote>() : new[] { fireplace },
                                      resourceLinkSet));

                if (fireplace is not null)
                {
                    col.Item().PaddingTop(8).Element(c => IncludedParagraph(c, new[] { fireplace }, request));

                    var optionalRows =
                        fireplace.OptionalFeatures
                            .Select(line => new OptionalPdfRow(PdfFeatureName(line.Feature), line.Description,
                                                               line.Price, FeaturePdfUrl(line, resourceLinkSet)))
                            .ToList();

                    if (optionalRows.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("Optional Features").FontSize(8.2f).Bold();
                        col.Item().Element(c => OptionalFeaturesTable(c, optionalRows));
                    }
                }

                col.Item()
                    .PaddingTop(8)
                    .Text("The listed prices are the Manufacturer's Suggested Retail Price (MSRP), valid for 30 " +
                          "days, and do not include venting or installation costs.")
                    .Bold()
                    .FontSize(8.0f)
                    .LineHeight(1.1f)
                    .FontColor(TextDark);
            });
    }

    private static void HeaderBlock(IContainer container, string logo)
    {
        container.Column(col =>
                         {
                             col.Spacing(5);

                             col.Item().Row(
                                 row =>
                                 {
                                     row.ConstantItem(3).Height(34).Background(FlareRed).Text(string.Empty);
                                     row.ConstantItem(7).Text(string.Empty);
                                     row.ConstantItem(122).Height(40).Element(
                                         c =>
                                         {
                                             if (File.Exists(logo))
                                                 c.Image(logo).FitArea();
                                             else
                                                 c.Text("FLARE\nFIREPLACES").Bold().FontSize(18).LineHeight(.85f);
                                         });
                                     row.RelativeItem().Text(string.Empty);
                                 });

                             col.Item().PaddingTop(2).LineHorizontal(.8f).LineColor(RuleGray);
                             col.Item().AlignCenter().Text(PdfContactLine()).FontSize(7.7f).FontColor(TextMuted);
                             col.Item().PaddingTop(2).Text("Quote Request").FontSize(10.2f).Bold();
                         });
    }

    private static void CustomerProjectDetails(IContainer container, QuoteRequest request, string quoteDate,
                                               string quoteNumber, PricedFireplaceQuote? fireplace = null)
    {
        var projectTitle = FirstNonBlank(fireplace?.ProjectName, request.ProjectName, request.FireplaceLocation,
                                         fireplace?.FireplaceLabel, " ");
        var projectAddress = FirstNonBlank(fireplace?.ProjectAddress, request.ProjectAddress, request.Postal, " ");

        container.Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                                                    {
                                                        c.ConstantColumn(78);
                                                        c.RelativeColumn(1.9f);
                                                        c.ConstantColumn(56);
                                                        c.RelativeColumn(.9f);
                                                    });

                            table.Cell()
                                .ColumnSpan(4)
                                .Element(DetailsHeaderBox)
                                .AlignCenter()
                                .Text(projectTitle)
                                .Bold()
                                .FontSize(7.7f)
                                .AlignCenter();

                            DetailsLabel(table, "Client Name:");
                            DetailsValue(table, request.ClientName);
                            DetailsLabel(table, "Date:");
                            DetailsValue(table, quoteDate);

                            DetailsLabel(table, "Project Address:");
                            DetailsValue(table, projectAddress);
                            DetailsLabel(table, "Quote #:");
                            DetailsValue(table, quoteNumber);
                        });
    }

    private static void PricingTable(IContainer container, IReadOnlyList<PricedFireplaceQuote> fireplaces,
                                     ResourceLinkSet? resourceLinkSet)
    {
        container.Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                                                    {
                                                        c.RelativeColumn(.95f);
                                                        c.RelativeColumn(2.2f);
                                                        c.RelativeColumn(.75f);
                                                        c.RelativeColumn(1.25f);
                                                    });

                            HeaderCell(table, "Model #");
                            HeaderCell(table, "Description");
                            HeaderCell(table, "MSRP");
                            HeaderCell(table, "Lead Time");

                            foreach (var fp in fireplaces)
                            {
                                var fireplaceUrl = FireplacePdfUrl(resourceLinkSet, fp);

                                BodyCellLink(table, FirstNonBlank(fp.ModelNumber, fp.BaseLine.Sku), fireplaceUrl);
                                BodyCellLink(table, FirstNonBlank(fp.Description, fp.BaseLine.Description),
                                             fireplaceUrl);
                                BodyCell(table, Money(fp.BaseLine.Price));
                                BodyCell(table, fp.LeadTime);
                            }
                        });
    }

    private static void OptionalFeaturesTable(IContainer container, IReadOnlyList<OptionalPdfRow> rows)
    {
        container.Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                                                    {
                                                        c.RelativeColumn(1.25f);
                                                        c.RelativeColumn(2.35f);
                                                        c.RelativeColumn(.75f);
                                                    });

                            HeaderCell(table, "Feature");
                            HeaderCell(table, "Description");
                            HeaderCell(table, "MSRP");

                            foreach (var row in rows)
                            {
                                BodyCellLink(table, row.Feature, row.Url, accent: true);
                                BodyCell(table, row.Description);
                                BodyCell(table, Money(row.Price));
                            }
                        });
    }

    private static void IncludedParagraph(IContainer container, IReadOnlyList<PricedFireplaceQuote> fireplaces,
                                          QuoteRequest request)
    {
        var first = fireplaces.FirstOrDefault();
        var included = first is null
                           ? new IncludedCopy("Included With Every Fireplace Purchase: ",
                                              "Power Supply, Wall Switch, Remote Control, Classic Media, and *Free " +
                                                  "Shipping (*Free shipping within the continental United States only)")
                           : IncludedText(first, request);

        container.Text(text =>
                       {
                           text.DefaultTextStyle(x => x.FontSize(7.1f).FontColor(TextDark).LineHeight(1.08f));
                           text.Span(included.Header).Bold();
                           text.Span(included.Body);
                       });
    }
    private static bool IsIndoorOutdoorSeeThroughIncludedCopy(PricedFireplaceQuote fp)
    {
        var combined = $"{fp.Type} {fp.Model} {fp.ModelNumber} {fp.Description}";
        var compact = Regex.Replace(combined, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return fp.Type == FireplaceType.IndoorOutdoorSeeThrough ||
               Regex.IsMatch(compact, @"ST\d{2,3}(EH|H|R)?(OD|IO)", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(compact, @"ST\d{2,3}(EH|H|R)?OD", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(compact, @"(STPASS|PASSST)(OD|IO)", RegexOptions.IgnoreCase) ||
               compact.Contains("INDOOROUTDOORSEETHROUGH", StringComparison.OrdinalIgnoreCase);
    }

    private static string IncludedClassicMediaText(params string?[] values)
    {
        foreach (var value in values)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var normalized = NormalizePdfText(text);
            if (normalized is "none" or "none selected" or "no classic media selected")
                continue;

            return text;
        }

        return "Classic Media";
    }

    private static IncludedCopy IncludedText(PricedFireplaceQuote fp, QuoteRequest request)
    {
        var media =
            IncludedClassicMediaText(fp.ClassicMediaDisplay, request.ClassicMedia.FirstOrDefault()?.DisplayName);

        // OD/IO included copy guard
        if (IsIndoorOutdoorSeeThroughIncludedCopy(fp))
            return new IncludedCopy(
                "Included With Every Indoor Outdoor See Through Fireplace Purchase: ",
                $"Power Supply, Wall Switch, Remote Control, {media}, Outdoor Kit, and *Free Shipping (*Free shipping within the continental United States only)");
        return fp.Type switch {
            FireplaceType.IndoorOutdoorSeeThrough => new IncludedCopy(
                "Included With Every Indoor Outdoor See Through Fireplace Purchase: ",
                $"Power Supply, Wall Switch, Remote Control, {media}, Outdoor Kit, and *Free Shipping (*Free shipping within the continental United States only)"),
            FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough => new IncludedCopy(
                "Every Outdoor Flare Fireplace Purchase Includes: ",
                $"Power Supply, {media}, RGB LEDs, and *Free Shipping (*USA Only, lift-gate fees may apply)"),
            FireplaceType.Large => new IncludedCopy(
                "Included With Every Large Indoor Fireplace Purchase: ",
                $"Power Supply, Wall Switch, Remote Control, {media}, Double Glass, Reflective Back, and *Free Shipping (*Free shipping within the continental United States only)"),
            FireplaceType.Traditional => new IncludedCopy(
                "Included With Every Traditional Fireplace Purchase: ",
                $"Power Supply, Wall Switch, Remote Control, {media}, RGB LEDs, and *Free Shipping (*Free shipping within the continental United States only)"),
            _ => new IncludedCopy(
                "Included With Every Indoor Fireplace Purchase: ",
                IsRoomDefiner(fp) && !IsSeeThroughForReflectiveBackRules(fp)
                    ? $"Power Supply, Wall Switch, Remote Control, {media}, Reflective Black Back, and *Free Shipping (*Free shipping within the continental United States only)"
                    : $"Power Supply, Wall Switch, Remote Control, {media}, and *Free Shipping (*Free shipping within the continental United States only)")
        };
    }

    private static bool IsSeeThroughForReflectiveBackRules(PricedFireplaceQuote fp)
    {
        if (fp.Type is FireplaceType.IndoorSeeThrough or FireplaceType.IndoorOutdoorSeeThrough or
                FireplaceType.OutdoorSeeThrough)
            return true;

        var compact = new string(string.Join(" ", fp.Model, fp.ModelNumber, fp.Description, fp.FireplaceLabel)
                                     .Where(char.IsLetterOrDigit)
                                     .ToArray())
                          .ToUpperInvariant();

        var text = string.Join(" ", fp.Model, fp.ModelNumber, fp.Description, fp.FireplaceLabel).ToLowerInvariant();

        return compact.StartsWith("ST") || compact.StartsWith("VST") || compact.StartsWith("VFST") ||
               text.Contains("see through") || text.Contains("see-through");
    }
    private static bool IsRoomDefiner(PricedFireplaceQuote fp)
    {
        var combined = string.Join(" ", fp.Model, fp.ModelNumber, fp.Description, fp.FireplaceLabel).ToLowerInvariant();
        combined = new string(combined.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        combined = string.Join(' ', combined.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return combined.Contains("room definer") || combined.Contains("dvdrd") || combined.Contains(" rd ") ||
               combined.EndsWith(" rd") || combined.StartsWith("rd ");
    }

    private static void DetailsLabel(TableDescriptor table, string value) =>
        table.Cell().Element(DetailsBox).AlignMiddle().Text(value).Bold().FontSize(6.8f).AlignLeft();

    private static void DetailsValue(TableDescriptor table,
                                     string value) => table.Cell()
                                                          .Element(DetailsBox)
                                                          .AlignMiddle()
                                                          .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
                                                          .FontSize(6.8f)
                                                          .AlignLeft();

    private static void HeaderCell(TableDescriptor table, string value) => table.Cell()
                                                                               .Element(TableHeaderBox)
                                                                               .AlignCenter()
                                                                               .AlignMiddle()
                                                                               .Text(value)
                                                                               .Bold()
                                                                               .FontSize(6.4f)
                                                                               .AlignCenter();

    private static void BodyCell(TableDescriptor table, string value,
                                 bool accent = false) => BodyCellLink(table, value, string.Empty, accent);

    private static void BodyCellLink(TableDescriptor table, string value, string? url, bool accent = false)
    {
        var display = value ?? string.Empty;
        var safeUrl = CleanPdfUrl(url);
        var color = accent ? FeatureBlue : TextDark;

        var cell = table.Cell().Element(TableBodyBox).AlignCenter().AlignMiddle();

        if (string.IsNullOrWhiteSpace(safeUrl))
        {
            cell.Text(display).FontSize(6.15f).FontColor(color).AlignCenter().LineHeight(1.0f);
            return;
        }

        cell.Hyperlink(safeUrl).Text(display).FontSize(6.15f).FontColor(color).AlignCenter().LineHeight(1.0f);
    }

    private static IContainer DetailsHeaderBox(IContainer c) =>
        c.Border(1).BorderColor(Colors.Black).PaddingVertical(1.1f).PaddingHorizontal(2).MinHeight(11).AlignMiddle();

    private static IContainer DetailsBox(IContainer c) => c.Border(1)
                                                              .BorderColor(Colors.Black)
                                                              .PaddingVertical(.85f)
                                                              .PaddingHorizontal(2.5f)
                                                              .MinHeight(10.5f)
                                                              .AlignMiddle();

    private static IContainer TableHeaderBox(IContainer c) => c.Border(1)
                                                                  .BorderColor(Colors.Black)
                                                                  .PaddingVertical(.85f)
                                                                  .PaddingHorizontal(2)
                                                                  .MinHeight(10.3f)
                                                                  .AlignCenter()
                                                                  .AlignMiddle();

    private static IContainer TableBodyBox(IContainer c) => c.Border(1)
                                                                .BorderColor(Colors.Black)
                                                                .PaddingVertical(.75f)
                                                                .PaddingHorizontal(2)
                                                                .MinHeight(10.6f)
                                                                .AlignCenter()
                                                                .AlignMiddle();

    private static string FindLogoPath()
    {
        var candidates =
            new[] { Path.Combine(AppContext.BaseDirectory, "Assets", "header_logo-light.png"),
                    Path.Combine(AppContext.BaseDirectory, "Assets", "header_logo.png"),
                    Path.Combine(Environment.CurrentDirectory, "FlareQuotes.App", "Assets", "header_logo-light.png"),
                    Path.Combine(Environment.CurrentDirectory, "FlareQuotes.App", "Assets", "header_logo.png"),
                    Path.Combine(Environment.CurrentDirectory, "Assets", "header_logo-light.png"),
                    Path.Combine(Environment.CurrentDirectory, "Assets", "header_logo.png") };

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static string PdfFeatureName(string feature)
    {
        var normalized = new string((feature ?? string.Empty)
                                        .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ')
                                        .ToArray());
        normalized = string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Contains("summit burner"))
            return "Summit Burner";
        if (normalized.Contains("double glass"))
            return "Double Glass";
        if (normalized.Contains("reflective black back"))
            return "Reflective Black Back";
        if (normalized.Contains("reflective black sides"))
            return "Reflective Black Sides";
        if (normalized.Contains("rgb"))
            return "RGB LEDs";
        if (normalized.Contains("summer kit"))
            return "Summer Kit";
        if (normalized.Contains("active heat flex"))
            return "Active Heat Flex";
        if (normalized.Contains("passive heat flex"))
            return "Passive Heat Flex";
        if (normalized.Contains("heat release louver"))
            return "Heat Release Louver";
        if (normalized.Contains("air intake louver"))
            return "Air Intake Louver";
        if (normalized.Contains("power vent"))
            return "Power Vent";
        if (normalized.Contains("reflective black interior"))
            return "Reflective Black Interior";

        return FirstNonBlank(feature, " ");
    }

    private static string DisplayDateShort(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed.ToString("MM/dd/yy", CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(value) ? DateTime.Now.ToString("MM/dd/yy", CultureInfo.InvariantCulture)
                                                : value;
    }
    private static ResourceLinkSet? FindMatchingResourceLinkSet(IReadOnlyList<ResourceLinkSet>? sets,
                                                                PricedFireplaceQuote fp)
    {
        if (sets is null || sets.Count == 0)
            return null;

        var candidates = new[] { fp.ModelNumber, fp.BaseLine.Sku, fp.Model, fp.Description }
                             .Select(NormalizePdfKey)
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();

        if (candidates.Count == 0)
            return sets.FirstOrDefault();

        foreach (var set in sets)
        {
            var setKey = NormalizePdfKey(set.ModelNumber);

            if (candidates.Any(candidate => setKey.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                                            setKey.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                                            candidate.Contains(setKey, StringComparison.OrdinalIgnoreCase)))
            {
                return set;
            }
        }

        return sets.FirstOrDefault();
    }

    private static string FireplacePdfUrl(ResourceLinkSet? set, PricedFireplaceQuote fp)
    {
        return FirstValidUrl(fp.BaseLine.Url,
                             PreferredResourceUrl(set, "Product Sheet", "Product", "3-Part Spec", "Specification",
                                                  "Spec", "Dimension File", "Dimensions", "Framing Guide",
                                                  "Wood Framing", "Metal Framing"));
    }

    private static string FeaturePdfUrl(PriceLine line, ResourceLinkSet? set)
    {
        return FirstValidUrl(line.Url, PreferredResourceUrl(set, line.Feature, line.Description, line.Sku),
                             FeatureFallbackUrl(line.Feature, line.Description, line.Sku));
    }

    private static string PreferredResourceUrl(ResourceLinkSet? set, params string?[] preferredLabels)
    {
        if (set?.Links is null || set.Links.Count == 0)
            return string.Empty;

        foreach (var label in preferredLabels.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (TryGetResourceUrl(set, label!, out var direct))
                return direct;
        }

        foreach (var label in new[] { "Product Sheet", "Product", "3-Part Spec", "Specification", "Spec",
                                      "Framing Guide", "Wood Framing", "Metal Framing", "Dimension File",
                                      "Dimensions" })
        {
            if (TryGetResourceUrl(set, label, out var fallback))
                return fallback;
        }

        return set.Links.Values.Select(CleanPdfUrl).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
               string.Empty;
    }

    private static bool TryGetResourceUrl(ResourceLinkSet set, string label, out string url)
    {
        url = string.Empty;
        var requested = NormalizePdfKey(label);

        foreach (var pair in set.Links)
        {
            var key = NormalizePdfKey(pair.Key);

            if (key.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(requested, StringComparison.OrdinalIgnoreCase) ||
                requested.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                url = CleanPdfUrl(pair.Value);
                if (!string.IsNullOrWhiteSpace(url))
                    return true;
            }
        }

        return false;
    }

    private static string FeatureFallbackUrl(params string?[] values)
    {
        var text = NormalizePdfText(string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))));

        if (text.Contains("power vent"))
            return "https://flarefireplaces.com/power-vent/";
        if (text.Contains("double glass") || text.Contains("safety barrier"))
            return "https://flarefireplaces.com/double-glass/";
        if (text.Contains("summit"))
            return "https://flarefireplaces.com/summit-burner/";
        if (text.Contains("summer kit"))
            return "https://flarefireplaces.com/summer-kit/";
        if (text.Contains("rgb") || text.Contains("led"))
            return "https://flarefireplaces.com/multicolor-led-lights/";
        if (text.Contains("active heat flex"))
            return "https://flarefireplaces.com/active-heat-flex/";
        if (text.Contains("passive heat flex"))
            return "https://flarefireplaces.com/passive-heat-flex/";
        if (text.Contains("heat release") || text.Contains("air intake") || text.Contains("louver"))
            return "https://flarefireplaces.com/free-flow-heat-release/";
        if (text.Contains("reflective"))
            return "https://flarefireplaces.com/reflective-back/";
        if (text.Contains("brick traditional") || text.Contains("herringbone") || text.Contains("offset"))
            return "https://flarefireplaces.com/traditional-fireplace/";
        if (text.Contains("media") || text.Contains("driftwood") || text.Contains("stone") || text.Contains("balls"))
            return "https://flarefireplaces.com/mediaoptions/traditional-media/";

        return string.Empty;
    }

    private static string FirstValidUrl(params string?[] values) =>
        values.Select(CleanPdfUrl).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string CleanPdfUrl(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   ? text
                   : string.Empty;
    }

    private static string NormalizePdfKey(string? value) =>
        Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

    private static string NormalizePdfText(string? value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", " ").Trim().ToLowerInvariant();
        return Regex.Replace(text, @"\s+", " ");
    }

    private static string Money(decimal? value) => value.HasValue
                                                       ? value.Value.ToString("C0", CultureInfo.GetCultureInfo("en-US"))
                                                       : string.Empty;

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private sealed record OptionalPdfRow(string Feature, string Description, decimal? Price, string Url);
    private sealed record IncludedCopy(string Header, string Body);

    private static string PdfContactLine()
    {
        var email = PdfContactEmail();
        var phone = PdfContactPhone();
        var website = PdfContactWebsite();

        var parts = new[] { email, phone, website }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

        return parts.Length == 0 ? "flarefireplaces.com" : string.Join(" | ", parts);
    }

    private static string PdfContactEmail()
    {
        return PdfSettingsValue("kyle@flarefireplaces.com", "SalesEmail", "SenderEmail", "Email", "EmailAddress",
                                "FromEmail", "GmailEmail", "UserEmail");
    }

    private static string PdfContactPhone()
    {
        return PdfSettingsValue("(512) 913-1687", "SalesPhone", "SenderPhone", "Phone", "PhoneNumber", "UserPhone",
                                "ContactPhone");
    }

    private static string PdfContactWebsite()
    {
        return CleanWebsiteValue(PdfSettingsValue("flarefireplaces.com", "Website", "CompanyWebsite", "WebSite",
                                                  "WebsiteUrl", "CompanyUrl", "FlareWebsite"));
    }

    private static string PdfSettingsValue(string fallback, params string[] propertyNames)
    {
        try
        {
            var liveValue = FirstSettingsProperty(AppSettingsRuntimeCache.Current, propertyNames);
            if (!string.IsNullOrWhiteSpace(liveValue))
                return liveValue.Trim();

            var settingsPath = FindPdfSettingsPath();

            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                return fallback;

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            var root = document.RootElement;

            foreach (var propertyName in propertyNames)
            {
                if (!root.TryGetProperty(propertyName, out var value))
                    continue;

                if (value.ValueKind != System.Text.Json.JsonValueKind.String)
                    continue;

                var text = value.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }
        catch
        {
            // PDF generation should never fail because settings are missing or malformed.
        }

        return fallback;
    }

    private static string? FirstSettingsProperty(AppSettings? settings, params string[] propertyNames)
    {
        if (settings is null)
            return null;

        var type = settings.GetType();

        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property is null)
                continue;

            var value = property.GetValue(settings) as string;

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? FindPdfSettingsPath() => AppPaths.SettingsFile;

    private static string CleanWebsiteValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        text = text.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Trim()
                   .TrimEnd('/');

        return text;
    }
}
