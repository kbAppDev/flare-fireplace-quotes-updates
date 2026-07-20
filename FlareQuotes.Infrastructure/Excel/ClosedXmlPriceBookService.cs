using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.Infrastructure.Excel;

public sealed class ClosedXmlPriceBookService : IPriceBookService
{
    private PriceBookWorkbook? _cached;
    private string _loadedPath = string.Empty;

    private static readonly Dictionary<string, decimal> LouverMsrpFallback =
        new(StringComparer.OrdinalIgnoreCase) { ["50"] = 266.6666666666667m,
                                                ["70"] = 333.3333333333334m,
                                                ["100"] = 400m,
                                                ["120"] = 433.3333333333334m,
                                                ["140"] = 466.6666666666667m,
                                                ["180"] = 566.6666666666667m,
                                                ["200"] = 666.6666666666667m };

    public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_cached is not null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(_cached);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult(new PriceBookWorkbook { SourcePath = path ?? string.Empty });

        using var workbook = new XLWorkbook(path);
        var result = new PriceBookWorkbook { SourcePath = path };
        foreach (var worksheet in workbook.Worksheets)
        {
            result.SheetNames.Add(worksheet.Name);
            var headers = worksheet.Row(1)
                              .CellsUsed()
                              .Select((cell, index) => new { Name = cell.GetString(), Index = index + 1 })
                              .ToList();
            foreach (var row in worksheet.RowsUsed().Skip(1))
            {
                var priceRow = new PriceRow { SheetName = worksheet.Name };
                var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? headers.Max(h => h.Index);
                for (var col = 1; col <= lastColumn; col++)
                {
                    priceRow.RawValues[$"Column{col}"] = row.Cell(col).GetFormattedString().Trim();
                }
                foreach (var h in headers)
                {
                    var header = h.Name.Trim();
                    if (string.IsNullOrWhiteSpace(header))
                        continue;
                    priceRow.RawValues[header] = row.Cell(h.Index).GetFormattedString().Trim();
                }
                priceRow.Sku = First(priceRow.RawValues, "SKU", "Sku", "Model #", "Part #", "Item #");
                priceRow.PartName = First(priceRow.RawValues, "Part Name", "Name", "Model", "Product");
                priceRow.Description = First(priceRow.RawValues, "Description", "Product Description", "Style");
                priceRow.Price = ParsePrice(First(priceRow.RawValues, "MSRP", "Price", "List Price"));
                if (!string.IsNullOrWhiteSpace(priceRow.Sku) || !string.IsNullOrWhiteSpace(priceRow.PartName) ||
                    !string.IsNullOrWhiteSpace(priceRow.Description) || IsQuantityCalculationSheet(worksheet.Name))
                    result.Rows.Add(priceRow);
            }
        }

        _cached = result;
        _loadedPath = path;
        return Task.FromResult(result);
    }

    private static bool IsQuantityCalculationSheet(string sheetName)
    {
        return sheetName.Contains("Media", StringComparison.OrdinalIgnoreCase) &&
               sheetName.Contains("Calculation", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request,
                                                         CancellationToken cancellationToken = default)
    {
        var path = GetDefaultPricingPath();
        var result = await BuildPricedQuoteAsync(request, path, cancellationToken);
        var first = result.Fireplaces.FirstOrDefault();
        return first?.BaseLine.Price is null
                   ? new PriceBookMatch { Found = false, Reason = result.Message }
                   : new PriceBookMatch { Found = true,
                                          Row = new PriceRow { Sku = first.BaseLine.Sku,
                                                               Description = first.BaseLine.Description,
                                                               Price = first.BaseLine.Price },
                                          Reason = "Matched" };
    }

    public async Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
                                                            CancellationToken cancellationToken = default)
    {
        var workbook = await LoadAsync(GetDefaultPricingPath(), cancellationToken);
        var model = request.Model ?? string.Empty;
        var size = request.Size ?? string.Empty;
        var glassHeight = request.GlassHeight ?? string.Empty;
        var row = FindFeatureRow(workbook, DetectType(model, size), model, size, glassHeight, feature.DisplayName);
        return row is null ? new PriceBookMatch { Found = false, Reason = $"No row found for {feature.DisplayName}" }
                           : new PriceBookMatch { Found = true, Row = row, Reason = "Matched" };
    }

    public async Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
                                                               CancellationToken cancellationToken = default)
    {
        var workbook = await LoadAsync(pricingPath, cancellationToken);
        if (workbook.Rows.Count == 0)
            return new PricedQuoteResult { Success = false, Request = request,
                                           Message = $"Pricing file not found or empty: {pricingPath}" };

        var result = new PricedQuoteResult { Request = request, Success = true };
        IEnumerable<FireplaceQuote> fireplaceInputs =
            request.Fireplaces.Count > 0 ? request.Fireplaces : new List<FireplaceQuote> { ToFireplace(request) };

        foreach (var input in fireplaceInputs)
        {
            var inputModel = input.Model ?? string.Empty;
            var inputSize = input.Size ?? string.Empty;
            var inputGlassHeight = input.GlassHeight ?? string.Empty;
            // block invalid Large See Through resource links
            if (IsInvalidLargeSeeThroughModel(inputModel, inputSize))
                continue;
            var type = input.Type == FireplaceType.Unknown ? DetectType(inputModel, inputSize) : input.Type;
            // Outdoor Kit type promotion
            type = ApplyIndoorOutdoorSeeThroughForOutdoorKit(type, inputModel, input.Features);
            var baseRow = FindBaseRow(workbook, type, inputModel, inputSize, inputGlassHeight);
            // OD/IO included outdoor kit row
            var includedOutdoorKitRow =
                FindIncludedOutdoorKitRow(workbook, type, inputModel, inputSize, inputGlassHeight);
            var modelNumber = ResolveModelNumber(workbook, type, inputModel, inputSize, inputGlassHeight) ??
                              BuildModelNumber(type, inputModel, inputSize, inputGlassHeight);
            var priced = new PricedFireplaceQuote {
                FireplaceLabel = BuildLabel(input),
                FireplaceLocation = input.FireplaceLocation,
                ProjectName = input.ProjectName,
                ProjectAddress = input.ProjectAddress,
                Type = type,
                Model = inputModel,
                Size = inputSize,
                GlassHeight = inputGlassHeight,
                ModelNumber = modelNumber,
                Description = baseRow?.Description ?? BuildDescription(type, inputModel, inputSize, inputGlassHeight),
                LeadTime = string.IsNullOrWhiteSpace(input.LeadTime) ? "3-5 Business Days" : input.LeadTime,
                ClassicMediaDisplay = !string.IsNullOrWhiteSpace(input.ClassicMediaDisplay)
                                          ? input.ClassicMediaDisplay
                                          : (request.ClassicMedia.FirstOrDefault()?.DisplayName ?? string.Empty),
                BaseLine =
                    new PriceLine { Feature = "Fireplace",
                                    Description = baseRow?.Description ??
                                                  BuildDescription(type, inputModel, inputSize, inputGlassHeight),
                                    Sku = FirstNonBlank(baseRow?.Sku, modelNumber),
                                    Price = AddPrices(baseRow?.Price, includedOutdoorKitRow?.Price),
                                    SourceSheet = baseRow?.SheetName ?? string.Empty, Url = PriceLineUrl(baseRow) }
            };

            foreach (var feature in input.Features)
            {
                // skip manual Outdoor Kit optional feature
                if (IsOutdoorKitFeature($"{feature.Key} {feature.DisplayName} {feature.PdfDescription}"))
                    continue;
                if (IsDoubleCorner(inputModel, inputSize, inputGlassHeight) &&
                    IsReflectiveSidesFeature(feature.DisplayName))
                    continue;

                if (IsRoomDefiner(inputModel, inputSize, inputGlassHeight) &&
                    IsRoomDefinerReflectiveFeature(feature.DisplayName))
                    continue;

                var row = FindFeatureRow(workbook, type, inputModel, inputSize, inputGlassHeight, feature.DisplayName);
                var featureDescription =
                    OverrideFeatureDescription(feature.DisplayName, row?.Description ?? feature.PdfDescription);

                if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
                {
                    var normalizedFeature = Normalize(feature.DisplayName);
                    if (normalizedFeature.Contains("safety screen"))
                        featureDescription = OutdoorVentFreeScreenDescription(type, inputModel);
                }
                priced.OptionalFeatures.Add(new PriceLine { Feature = CanonicalFeatureName(feature.DisplayName),
                                                            Description = featureDescription,
                                                            Sku = row?.Sku ?? string.Empty, Price = row?.Price,
                                                            SourceSheet = row?.SheetName ?? string.Empty,
                                                            Url = PriceLineUrl(row) });
            }

            foreach (var media in input.PremiumMedia)
            {
                if (IsStoneBallsMedia(media))
                {
                    priced.OptionalFeatures.Add(BuildStoneBallsPriceLine(media, inputSize));
                    continue;
                }

                if (IsDriftwoodMedia(media))
                {
                    var driftwoodLine = BuildDriftwoodPriceLine(workbook, type, inputSize, inputModel);
                    if (!media.IsPremium)
                    {
                        driftwoodLine.Feature = "Add. Classic Media - Driftwood";
                        driftwoodLine.Description =
                            driftwoodLine.Description.Replace("Driftwood Logs", "Add. Classic Media - Driftwood Logs",
                                                              StringComparison.OrdinalIgnoreCase);
                    }

                    priced.OptionalFeatures.Add(driftwoodLine);
                    continue;
                }

                var row = FindPremiumMediaRow(workbook, media.Key, media.DisplayName);
                var quantity = CalculatePremiumMediaQuantity(workbook, type, media, inputSize, inputModel);
                var mediaFeatureName =
                    media.IsPremium ? media.DisplayName : $"Add. Classic Media - {media.DisplayName}";
                priced.OptionalFeatures.Add(
                    new PriceLine { Feature = mediaFeatureName,
                                    Description =
                                        quantity > 1 ? $"{mediaFeatureName} - {quantity} sets" : mediaFeatureName,
                                    Sku = row?.Sku ?? string.Empty, Quantity = quantity,
                                    Price = row?.Price is null ? null : row.Price.Value * quantity,
                                    SourceSheet = row?.SheetName ?? string.Empty, Url = PriceLineUrl(row) });
            }

            if (priced.BaseLine.Price is null)
            {
                result.Success = false;
                result.Message += $"Could not price {priced.FireplaceLabel}. ";
            }
            result.Fireplaces.Add(priced);
        }

        result.ResourceLinks = await ResolveResourceLinksAsync(request, pricingPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Message))
            result.Message = result.Success ? "Pricing complete." : "Pricing completed with missing rows.";
        return result;
    }

    public async Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(
        QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default)
    {
        var workbook = await LoadResourceWorkbookAsync(pricingPath, cancellationToken);
        IEnumerable<FireplaceQuote> fireplaceInputs =
            request.Fireplaces.Count > 0 ? request.Fireplaces : new List<FireplaceQuote> { ToFireplace(request) };
        var results = new List<ResourceLinkSet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in fireplaceInputs)
        {
            var inputModel = input.Model ?? string.Empty;
            var inputSize = input.Size ?? string.Empty;
            var inputGlassHeight = input.GlassHeight ?? string.Empty;
            // block invalid Large See Through resource links
            if (IsInvalidLargeSeeThroughModel(inputModel, inputSize))
                continue;
            var type = input.Type == FireplaceType.Unknown ? DetectType(inputModel, inputSize) : input.Type;
            // Outdoor Kit type promotion
            type = ApplyIndoorOutdoorSeeThroughForOutdoorKit(type, inputModel, input.Features);
            var linkRow = TryGetOutdoorVentFreeResourceRowFromWorkbook(type, inputModel, inputSize, inputGlassHeight) ??
                          TryGetLargeResourceRow(type, inputModel, inputSize, inputGlassHeight) ??
                          FindResourceRow(workbook, type, inputModel, inputSize, inputGlassHeight);
            // Requested resource input model marker
            if (linkRow is not null)
                linkRow.RawValues["Requested Model"] = type == FireplaceType.IndoorOutdoorSeeThrough ||
                                                               IsIndoorOutdoorSeeThroughModelCode(inputModel) ||
                                                               HasOutdoorKitFeature(input.Features)
                                                           ? $"{inputModel} indoor outdoor outdoor kit"
                                                           : inputModel;
            var modelNumber = linkRow is not null ? First(linkRow.RawValues, "Model #", "Model Number", "Model")
                                                  : BuildModelNumber(type, inputModel, inputSize, inputGlassHeight);

            if (string.IsNullOrWhiteSpace(modelNumber))
                modelNumber = BuildModelNumber(type, inputModel, inputSize, inputGlassHeight);

            // Passage resource model-number override
            if (IsPassageModel(inputModel))
                modelNumber = PassageModelCode(inputModel);
            if (!seen.Add(modelNumber))
                continue;

            var set = new ResourceLinkSet { ModelNumber = modelNumber };
            var columns = ResourceColumns(type);
            var rowFallback = linkRow is not null ? CleanCellUrl(First(linkRow.RawValues, "Fallback URL", "Fallback",
                                                                       "Download Center", "Download Center URL"))
                                                  : string.Empty;
            var fallback = string.IsNullOrWhiteSpace(rowFallback) ? FallbackUrl(type) : rowFallback;

            foreach (var col in columns)
            {
                var url = linkRow is not null ? ResourceUrl(linkRow, col) : string.Empty;
                set.Links[col] = string.IsNullOrWhiteSpace(url) ? fallback : url;
                set.Sources[col] = string.IsNullOrWhiteSpace(url) ? "fallback" : "specific";
            }

            results.Add(set);
        }

        return results;
    }

    private async Task<PriceBookWorkbook> LoadResourceWorkbookAsync(string pricingPath,
                                                                    CancellationToken cancellationToken)
    {
        var primary = await LoadAsync(pricingPath, cancellationToken);
        if (HasResourceLinks(primary))
            return primary;

        foreach (var candidate in ResourceWorkbookCandidates(pricingPath))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;

            var workbook = await LoadAsync(candidate, cancellationToken);
            if (HasResourceLinks(workbook))
                return workbook;
        }

        var downloaded = await TryDownloadSharedResourceWorkbookAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
        {
            var workbook = await LoadAsync(downloaded, cancellationToken);
            if (HasResourceLinks(workbook))
                return workbook;
        }

        // No Resource Links sheet found anywhere. Return the primary workbook so the caller can safely fall back.
        return primary;
    }

    private static IEnumerable<string> ResourceWorkbookCandidates(string pricingPath)
    {
        var names = new[] { "resource_links.xlsx", "Resource Links.xlsx", "shared_pricing.xlsx",
                            "pricing_with_resource_links.xlsx", "pricing-resource-links.xlsx" };

        var directories = new List<string>();

        var pricingDir = string.IsNullOrWhiteSpace(pricingPath)
                             ? string.Empty
                             : Path.GetDirectoryName(Path.GetFullPath(pricingPath)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(pricingDir))
            directories.Add(pricingDir);

        directories.Add(Path.Combine(Environment.CurrentDirectory, "LocalData"));
        directories.Add(Path.Combine(AppContext.BaseDirectory, "LocalData"));
        directories.Add(AppPaths.Cache);

        foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in names)
                yield return Path.Combine(directory, name);
        }
    }

    private static async Task<string> TryDownloadSharedResourceWorkbookAsync(CancellationToken cancellationToken)
    {
        const string sharedPricingExportUrl =
            "https://docs.google.com/spreadsheets/d/1kBfDyekOABQckF22v1mXzk59apccLBI1GCWi2CHO9zA/export?format=xlsx";

        try
        {
            var cacheDir = AppPaths.Cache;
            Directory.CreateDirectory(cacheDir);
            var cachePath = Path.Combine(cacheDir, "shared_pricing.xlsx");

            if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(6))
                return cachePath;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await client.GetByteArrayAsync(sharedPricingExportUrl, cancellationToken);
            if (bytes.Length > 0)
                await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);

            return cachePath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasResourceLinks(PriceBookWorkbook workbook) =>
        workbook.Rows.Any(r => IsResourceLinksSheet(r.SheetName) && !string.IsNullOrWhiteSpace(ResourceModelKey(r)));
    private static bool IsOutdoorKitFeature(string? value)
    {
        var normalized = Normalize(value ?? string.Empty);
        var compact = Compact(value ?? string.Empty);

        return normalized.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("OUTDOORKIT", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("indoor outdoor", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("INDOOROUTDOOR", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("outdoor conversion", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("od kit", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("ODST", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("ODPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("ODPAST", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOutdoorKitFeature(IEnumerable<FeatureSelection>? features)
    {
        return features?.Any(feature => IsOutdoorKitFeature(
                                 $"{feature.Key} {feature.DisplayName} {feature.PdfDescription}")) == true;
    }

    private static FireplaceType ApplyIndoorOutdoorSeeThroughForOutdoorKit(FireplaceType type, string? model,
                                                                           IEnumerable<FeatureSelection>? features)
    {
        // ApplyIndoorOutdoorSeeThroughForOutdoorKit OD/IO model code
        if (IsIndoorOutdoorSeeThroughModelCode(model))
            return FireplaceType.IndoorOutdoorSeeThrough;
        if (HasOutdoorKitFeature(features) && IsSeeThroughForReflectiveBackRules(type, model) &&
            type is not(FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough))
        {
            return FireplaceType.IndoorOutdoorSeeThrough;
        }

        return type;
    }
    private static bool IsIndoorOutdoorSeeThroughModelCode(string? model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return Regex.IsMatch(compact, @"^ST(OD|IO)$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(compact, @"^STPASS(OD|IO)$", RegexOptions.IgnoreCase);
    }

    private static decimal? AddPrices(decimal? basePrice, decimal? includedPrice)
    {
        if (basePrice is null && includedPrice is null)
            return null;

        return (basePrice ?? 0m) + (includedPrice ?? 0m);
    }

    private static FireplaceQuote ToFireplace(QuoteRequest request) => new() {
        FireplaceLocation = request.FireplaceLocation,
        Type = ApplyIndoorOutdoorSeeThroughForOutdoorKit(DetectType(request.Model, request.Size), request.Model,
                                                         request.SelectedFeatures),
        Model = request.Model,
        Size = request.Size,
        GlassHeight = request.GlassHeight,
        LeadTime = "3-5 Business Days",
        Features = request.SelectedFeatures.ToList(),
        PremiumMedia = request.PremiumMedia.ToList()
    };

    private static FireplaceType ForceOutdoorVentFreeTypeFromModel(FireplaceType type, string? model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var text = Regex.Replace(model ?? string.Empty, @"[\-_]+", " ").ToLowerInvariant();

        if (compact.StartsWith("VST") || compact.StartsWith("VFST") || text.Contains("vent free see through") ||
            text.Contains("ventless see through"))
            return FireplaceType.OutdoorSeeThrough;

        if (compact.StartsWith("VFF") || compact.StartsWith("VFFF") || compact.StartsWith("VLC") ||
            compact.StartsWith("VRC") || compact.StartsWith("VDC") || compact.StartsWith("VF") ||
            text.Contains("vent free") || text.Contains("ventless"))
            return FireplaceType.Outdoor;

        return type;
    }

    private static PriceRow? FindExactLargeBaseRow(IEnumerable<PriceRow> rows, FireplaceType type, string model,
                                                   string size, string glassHeight)
    {
        var sizeNum = Digits(size);
        if (string.IsNullOrWhiteSpace(sizeNum))
            return null;

        var style = StyleCode(type, model);
        var effectiveGlassHeight = EffectiveGlassHeight(glassHeight, model);
        var suffix = GlassSuffix(effectiveGlassHeight);

        var expectedPartName =
            string.IsNullOrWhiteSpace(suffix) ? $"FLARE-{style}-{sizeNum}" : $"FLARE-{style}-{sizeNum}-{suffix}";

        var expectedSku = $"LDV{style}{sizeNum}{suffix}";
        var compactExpectedPartName = Compact(expectedPartName);
        var compactExpectedSku = Compact(expectedSku);

        return Best(rows,
                    r =>
                    {
                        if (!Eq(r.SheetName, "Large Price Book"))
                            return false;

                        var rowText = Text(r);
                        if (rowText.Contains("reflective", StringComparison.OrdinalIgnoreCase) ||
                            rowText.Contains("summer kit", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var compactPartName = Compact(r.PartName);
                        var compactSku = Compact(r.Sku);
                        var compactText = Compact(rowText);

                        return compactPartName.Equals(compactExpectedPartName, StringComparison.OrdinalIgnoreCase) ||
                               compactSku.Equals(compactExpectedSku, StringComparison.OrdinalIgnoreCase) ||
                               compactText.Contains(compactExpectedPartName, StringComparison.OrdinalIgnoreCase) ||
                               compactText.Contains(compactExpectedSku, StringComparison.OrdinalIgnoreCase);
                    });
    }

    private static string Compact(string? value) =>
        Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();
    private static PriceRow? FindBaseRow(PriceBookWorkbook wb, FireplaceType type, string model, string size,
                                         string glassHeight)
    {
        // block invalid Large See Through base row
        if (IsInvalidLargeSeeThroughModel(model, size))
            return null;
        type = ForceOutdoorVentFreeTypeFromModel(type, model);
        var sheet = type switch { FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough => "Outdoor Price Book",
                                  FireplaceType.Large => "Large Price Book",
                                  _ => "Indoor Price Book" };

        var rows = wb.Rows.Where(r => Eq(r.SheetName, sheet) && r.Price is not null).ToList();
        var style = StyleWords(type, model);
        var sizeNum = FirstNonBlank(Digits(size), SizeDigitsFromModelCode(model));
        var glassNum = FirstNonBlank(GlassInches(glassHeight), GlassInches(ExtractGlassHeightFromModelCode(model)));

        // Passage base-row override
        if (IsPassageModel(model))
        {
            var passageBaseRow = FindPassageBaseRow(rows, model);
            if (passageBaseRow is not null)
                return passageBaseRow;
        }
        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            var vfCode = VentFreeModelStyleCode(type, model);
            var suffix = GlassSuffix(glassHeight);

            var part =
                string.IsNullOrWhiteSpace(suffix) ? $"FLARE-{vfCode}-{sizeNum}" : $"FLARE-{vfCode}-{sizeNum}-{suffix}";

            var skuStyle = vfCode switch { "VST" => "VFST", "VLC" => "VFLC", "VRC" => "VFRC", "VDC" => "VFDC",
                                           _ => "VFFF" };

            var sku = $"{skuStyle}{sizeNum}{suffix}";
            var compactPart = part.Replace("-", string.Empty);

            var exactOutdoor =
                Best(rows, r =>
                           {
                               var text = Text(r);
                               var compactText = text.Replace("-", string.Empty).Replace(" ", string.Empty);

                               return Eq(r.SheetName, "Outdoor Price Book") &&
                                      (text.Contains(part, StringComparison.OrdinalIgnoreCase) ||
                                       text.Contains(sku, StringComparison.OrdinalIgnoreCase) ||
                                       compactText.Contains(compactPart, StringComparison.OrdinalIgnoreCase) ||
                                       compactText.Contains(sku, StringComparison.OrdinalIgnoreCase)) &&
                                      text.Contains("vent free", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("safety", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("screen", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("wind guard", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("rgb", StringComparison.OrdinalIgnoreCase);
                           });
            if (exactOutdoor is not null)
                return exactOutdoor;

            // Outdoor Vent Free should never fall back to indoor ST/FF rows.
            return Best(rows, r => Text(r).Contains("vent free", StringComparison.OrdinalIgnoreCase) &&
                                   ContainsAll(Text(r), style) && ContainsSize(r, sizeNum) &&
                                   (string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum)) &&
                                   !Text(r).Contains("safety", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("screen", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("wind guard", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("rgb", StringComparison.OrdinalIgnoreCase));
        }

        if (type == FireplaceType.Traditional)
            return Best(rows, r => Text(r).Contains("traditional", StringComparison.OrdinalIgnoreCase) &&
                                   ContainsSize(r, sizeNum) &&
                                   !Text(r).Contains("double glass", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("brick", StringComparison.OrdinalIgnoreCase));

        if (type == FireplaceType.Large)
        {
            var exactLarge = FindExactLargeBaseRow(rows, type, model, sizeNum, glassHeight);
            if (exactLarge is not null)
                return exactLarge;

            return Best(rows, r => ContainsAll(Text(r), style) && ContainsSize(r, sizeNum) &&
                                   (string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum)) &&
                                   !Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("summer kit", StringComparison.OrdinalIgnoreCase));
        }

        return Best(rows, r => ContainsAll(Text(r), style) && ContainsSize(r, sizeNum) &&
                               (string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum)) &&
                               !Text(r).Contains("double glass", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("summit", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("safety", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("screen", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("wind guard", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("rgb", StringComparison.OrdinalIgnoreCase));
    }
    private static PriceRow? FindFeatureRow(PriceBookWorkbook wb, FireplaceType type, string model, string size,
                                            string glassHeight, string feature)
    {
        // block invalid Large See Through feature row
        if (IsInvalidLargeSeeThroughModel(model, size))
            return null;
        glassHeight = EffectiveGlassHeight(glassHeight, model);
        var f = Normalize(feature);
        var rows = wb.Rows.Where(r => r.Price is not null).ToList();
        var sizeNum = Digits(size);
        var glassNum = GlassInches(glassHeight);
        var style = StyleCode(type, model);
        var styleWords = StyleWords(type, model);

        // Passage feature-row override
        if (IsPassageModel(model))
        {
            var passageFeatureRow = FindPassageFeatureRow(rows, model, feature);
            if (passageFeatureRow is not null)
                return passageFeatureRow;
        }
        if (f.Contains("power vent"))
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   Text(r).Contains("power vent fan", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("cable", StringComparison.OrdinalIgnoreCase));

        if (f.Contains("summer kit"))
        {
            if (type == FireplaceType.Large)
            {
                var kits = LargeSummerKitCount(sizeNum);
                return Best(rows, r => Eq(r.SheetName, "Large Price Book") &&
                                       Text(r).Contains($"SK-Lrg-{kits}", StringComparison.OrdinalIgnoreCase) &&
                                       !Text(r).Contains("EOL", StringComparison.OrdinalIgnoreCase));
            }
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   Text(r).Contains("summer kit fan", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("End-of-Line", StringComparison.OrdinalIgnoreCase) &&
                                   !Text(r).Contains("EOL", StringComparison.OrdinalIgnoreCase));
        }

        if (f.Contains("reflective black interior"))
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   (Text(r).Contains($"RB-TRA-{sizeNum}", StringComparison.OrdinalIgnoreCase) ||
                                    Text(r).Contains($"RBTRA{sizeNum}", StringComparison.OrdinalIgnoreCase)));

        if (f.Contains("double glass"))
        {
            if (type == FireplaceType.Traditional)
                return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                       Text(r).Contains($"CLG-TRA-{sizeNum}", StringComparison.OrdinalIgnoreCase));
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   Text(r).Contains("double glass", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains(style, StringComparison.OrdinalIgnoreCase) &&
                                   ContainsSize(r, sizeNum) &&
                                   (string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum)));
        }

        if (f.Contains("summit"))
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   Text(r).Contains("summit burner", StringComparison.OrdinalIgnoreCase) &&
                                   ContainsSize(r, sizeNum));

        if (f.Contains("rgb"))
            return Best(rows, r => Eq(r.SheetName, type == FireplaceType.Large ? "Large Price Book"
                                                   : type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough
                                                       ? "Outdoor Price Book"
                                                       : "Indoor Price Book") &&
                                   Text(r).Contains("rgb", StringComparison.OrdinalIgnoreCase) &&
                                   (ContainsSize(r, sizeNum) ||
                                    type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough));

        if (f.Contains("reflective black sides"))
            return FindReflectiveSidesRow(rows, type, model, glassHeight);

        // Outdoor Kit FindFeatureRow
        if (IsOutdoorKitFeature(f))
            return FindOutdoorKitRow(rows, type, model, sizeNum, glassHeight);
        if (f.Contains("reflective black back") || f.Contains("reflective back"))
        {
            if (IsSeeThroughForReflectiveBackRules(type, model))
                return null;

            return FindReflectiveBackRow(rows, type, model, sizeNum, glassHeight);
        }

        if (f.Contains("brick"))
            return FindTraditionalBrickRow(rows, feature, sizeNum);

        if (f.Contains("safety screen"))
            return FindOutdoorSafetyScreenRow(rows, type, model, sizeNum, glassHeight);

        if (f.Contains("passive heat flex"))
            return FindPassiveHeatFlexRow(rows, type, model, sizeNum);

        if (f.Contains("active heat flex"))
            return FindActiveHeatFlexRow(rows, type, model, sizeNum);

        if (f.Contains("heat release louver"))
            return FindLouverRowBySize(wb, RecommendedHeatReleaseLouverSize(type, sizeNum, model));

        if (f.Contains("air intake louver"))
            return FindLouverRowBySize(wb, RecommendedAirIntakeLouverSize(type, sizeNum, model));

        return null;
    }
    private static string SizeDigitsFromModelCode(string? model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        var st = Regex.Match(compact, @"ST(\d{2,3})", RegexOptions.IgnoreCase);
        if (st.Success)
            return st.Groups[1].Value;

        var any = Regex.Match(compact, @"(\d{2,3})");
        return any.Success ? any.Groups[1].Value : string.Empty;
    }

    private static PriceRow? FindIncludedOutdoorKitRow(PriceBookWorkbook wb, FireplaceType type, string model,
                                                       string size, string glassHeight)
    {
        // style-only STOD/STIO outdoor kit lookup
        if (IsIndoorOutdoorSeeThroughModelCode(model) && !IsPassageModel(model))
        {
            var odRowsForIncludedKit = wb.Rows.Where(r => r.Price is not null).ToList();
            var odSizeNumForIncludedKit = Digits(size);
            var odEffectiveGlassForIncludedKit = EffectiveGlassHeight(glassHeight, model);
            var odGlassNumForIncludedKit = GlassInches(odEffectiveGlassForIncludedKit);
            var odSuffixForIncludedKit = GlassSuffix(odEffectiveGlassForIncludedKit);
            var odSkuSuffixForIncludedKit =
                odSuffixForIncludedKit.Equals("EH", StringComparison.OrdinalIgnoreCase) ? "E" : odSuffixForIncludedKit;

            var odExactPartForIncludedKit = string.IsNullOrWhiteSpace(odSuffixForIncludedKit)
                                                ? $"OD-ST-{odSizeNumForIncludedKit}"
                                                : $"OD-ST-{odSizeNumForIncludedKit}-{odSuffixForIncludedKit}";

            var odExactSkuForIncludedKit = $"ODDVST{odSizeNumForIncludedKit}{odSkuSuffixForIncludedKit}";
            var odExactPartCompactForIncludedKit = Compact(odExactPartForIncludedKit);
            var odExactSkuCompactForIncludedKit = Compact(odExactSkuForIncludedKit);

            var odExactIncludedKitRow = Best(
                odRowsForIncludedKit,
                r =>
                {
                    if (!Eq(r.SheetName, "Indoor Price Book"))
                        return false;

                    var text = Text(r);
                    var compactText = Compact(text);

                    return text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) &&
                           (compactText.Contains(odExactPartCompactForIncludedKit,
                                                 StringComparison.OrdinalIgnoreCase) ||
                            compactText.Contains(odExactSkuCompactForIncludedKit, StringComparison.OrdinalIgnoreCase));
                });

            if (odExactIncludedKitRow is not null)
                return odExactIncludedKitRow;
        }
        if (type != FireplaceType.IndoorOutdoorSeeThrough && !IsIndoorOutdoorSeeThroughModelCode(model))
            return null;

        var rows = wb.Rows.Where(r => r.Price is not null).ToList();
        var compactModel = Compact(model);

        if (IsPassageModel(model) || Regex.IsMatch(compactModel, @"^(STPASS|PASSST)(OD|IO)?$", RegexOptions.IgnoreCase))
        {
            return Best(rows, r =>
                              {
                                  if (!Eq(r.SheetName, "Indoor Price Book"))
                                      return false;

                                  var text = Text(r);
                                  var compactText = Compact(text);

                                  return text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) &&
                                         text.Contains("passage", StringComparison.OrdinalIgnoreCase) &&
                                         (compactText.Contains("ODSTPASS", StringComparison.OrdinalIgnoreCase) ||
                                          compactText.Contains("ODPAST", StringComparison.OrdinalIgnoreCase) ||
                                          compactText.Contains("OUTDOORKITFORSEETHROUGHPASSAGE",
                                                               StringComparison.OrdinalIgnoreCase));
                              });
        }

        var sizeNum = FirstNonBlank(Digits(size), SizeDigitsFromModelCode(model));
        var effectiveGlass = EffectiveGlassHeight(glassHeight, model);
        var glassNum = GlassInches(effectiveGlass);
        var suffix = GlassSuffix(effectiveGlass);
        var skuSuffix = suffix.Equals("EH", StringComparison.OrdinalIgnoreCase) ? "E" : suffix;

        var exactPart = string.IsNullOrWhiteSpace(suffix) ? $"OD-ST-{sizeNum}" : $"OD-ST-{sizeNum}-{suffix}";

        var exactSku = $"ODDVST{sizeNum}{skuSuffix}";
        var exactPartCompact = Compact(exactPart);
        var exactSkuCompact = Compact(exactSku);

        var exact = Best(rows, r =>
                               {
                                   if (!Eq(r.SheetName, "Indoor Price Book"))
                                       return false;

                                   var text = Text(r);
                                   var compactText = Compact(text);

                                   return text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) &&
                                          (compactText.Contains(exactPartCompact, StringComparison.OrdinalIgnoreCase) ||
                                           compactText.Contains(exactSkuCompact, StringComparison.OrdinalIgnoreCase));
                               });

        if (exact is not null)
            return exact;

        return Best(rows, r =>
                          {
                              if (!Eq(r.SheetName, "Indoor Price Book"))
                                  return false;

                              var text = Text(r);

                              if (!text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (!text.Contains("see", StringComparison.OrdinalIgnoreCase) ||
                                  !text.Contains("through", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (text.Contains("passage", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (!ContainsSize(r, sizeNum))
                                  return false;

                              return string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum);
                          });
    }
    private static PriceRow? FindOutdoorKitRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                               string sizeNum, string glassHeight)
    {
        if (!IsSeeThroughForReflectiveBackRules(type, model) && !IsPassageModel(model))
            return null;

        if (IsPassageModel(model))
        {
            return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                                   Text(r).Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("passage", StringComparison.OrdinalIgnoreCase));
        }

        var glassNum = GlassInches(EffectiveGlassHeight(glassHeight, model));

        return Best(rows, r =>
                          {
                              if (!Eq(r.SheetName, "Indoor Price Book"))
                                  return false;

                              var text = Text(r);

                              if (!text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (!text.Contains("see", StringComparison.OrdinalIgnoreCase) ||
                                  !text.Contains("through", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (text.Contains("passage", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (!ContainsSize(r, sizeNum))
                                  return false;

                              return string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum);
                          });
    }

    private static PriceRow? FindTraditionalBrickRow(IReadOnlyList<PriceRow> rows, string feature, string sizeNum)
    {
        var f = Normalize(feature);
        return Best(rows, r =>
                          {
                              if (!Eq(r.SheetName, "Indoor Price Book"))
                                  return false;
                              var text = Text(r);
                              if (!text.Contains("Brick", StringComparison.OrdinalIgnoreCase))
                                  return false;
                              if (!ContainsSize(r, sizeNum))
                                  return false;

                              var isHerringbone = f.Contains("herringbone");
                              if (isHerringbone && !text.Contains("Herringbone", StringComparison.OrdinalIgnoreCase))
                                  return false;
                              if (!isHerringbone && text.Contains("Herringbone", StringComparison.OrdinalIgnoreCase))
                                  return false;

                              if (f.Contains("light stone"))
                                  return text.Contains("Light Stone", StringComparison.OrdinalIgnoreCase);
                              if (f.Contains("natural"))
                                  return text.Contains("Natural", StringComparison.OrdinalIgnoreCase);
                              if (f.Contains("black"))
                                  return text.Contains("Black", StringComparison.OrdinalIgnoreCase);
                              if (f.Contains("red"))
                                  return text.Contains("Red", StringComparison.OrdinalIgnoreCase);
                              return false;
                          });
    }

    private static PriceRow? FindPassiveHeatFlexRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                                    string sizeNum)
    {
        var target = RecommendedPassiveHeatFlexSize(type, model, sizeNum);
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var exactSku = $"HFPK-{target}";
        var exactSkuNoDash = $"HFPK{target}";
        return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                               Text(r).Contains("passive", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("heat", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("flex", StringComparison.OrdinalIgnoreCase) &&
                               (Text(r).Contains(exactSku, StringComparison.OrdinalIgnoreCase) ||
                                Text(r).Contains(exactSkuNoDash, StringComparison.OrdinalIgnoreCase)));
    }

    private static PriceRow? FindActiveHeatFlexRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                                   string sizeNum)
    {
        var target = RecommendedActiveHeatFlexRange(type, model, sizeNum);
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var sku = target == "8010" ? "HFAK-8010-IN" : "HFAK-2570-IN";
        var skuNoDash = target == "8010" ? "HFAK8010I" : "HFAK2570I";
        return Best(rows, r => Eq(r.SheetName, "Indoor Price Book") &&
                               Text(r).Contains("active", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("heat", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("flex", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("inline", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("end of line", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("end-of-line", StringComparison.OrdinalIgnoreCase) &&
                               !Text(r).Contains("EOL", StringComparison.OrdinalIgnoreCase) &&
                               (Text(r).Contains(sku, StringComparison.OrdinalIgnoreCase) ||
                                Text(r).Contains(skuNoDash, StringComparison.OrdinalIgnoreCase)));
    }

    private static string RecommendedPassiveHeatFlexSize(FireplaceType type, string model, string sizeNum)
    {
        var normalizedModel = Normalize(model);
        if (type == FireplaceType.Traditional || normalizedModel.Contains("traditional"))
            return "100";
        if (normalizedModel.Contains("passage"))
            return "70";
        if (!int.TryParse(sizeNum, out var sizeInt))
            return string.Empty;
        if (sizeInt <= 30)
            return "70";
        if (sizeInt is 42 or 45 or 46 or 50)
            return "100";
        if (sizeInt is 60 or 70)
            return "120";
        if (sizeInt is 80 or 100)
            return "180";
        return string.Empty;
    }

    private static string RecommendedActiveHeatFlexRange(FireplaceType type, string model, string sizeNum)
    {
        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough or FireplaceType.Large)
            return string.Empty;
        if (!int.TryParse(sizeNum, out var sizeInt))
            return string.Empty;
        if (sizeInt >= 80 && sizeInt <= 100)
            return "8010";
        if (sizeInt >= 25 && sizeInt <= 70)
            return "2570";
        return string.Empty;
    }

    private static string VentFreeModelStyleCode(FireplaceType type, string model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var normalized = Normalize(model ?? string.Empty);

        if (compact.StartsWith("VST") || compact.StartsWith("VFST"))
            return "VST";
        if (compact.StartsWith("VLC"))
            return "VLC";
        if (compact.StartsWith("VRC"))
            return "VRC";
        if (compact.StartsWith("VDC"))
            return "VDC";
        if (compact.StartsWith("VFF") || compact.StartsWith("VF"))
            return "VFF";

        if (type == FireplaceType.OutdoorSeeThrough || normalized.Contains("see through") ||
            normalized.Contains("see-through") || normalized.Contains("st"))
            return "VST";

        if (normalized.Contains("left") || normalized.Contains("lc"))
            return "VLC";
        if (normalized.Contains("right") || normalized.Contains("rc"))
            return "VRC";
        if (normalized.Contains("double") || normalized.Contains("dc"))
            return "VDC";

        return "VFF";
    }

    private static string NormalizeResourceKey(string? value)
    {
        return Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
    }

    private static PriceRow? FindOutdoorSafetyScreenRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                                        string sizeNum, string glassHeight)
    {
        if (string.IsNullOrWhiteSpace(sizeNum))
            return null;

        var style = StyleCode(type, model);
        var suffix = GlassSuffix(glassHeight);
        var glassNum = Digits(glassHeight);

        var part = string.IsNullOrWhiteSpace(suffix) ? $"VF-SCREEN-{style}-{sizeNum}"
                                                     : $"VF-SCREEN-{style}-{sizeNum}-{suffix}";

        var sku = $"VSC{style}{sizeNum}{suffix}";
        var compactPart = part.Replace("-", string.Empty);

        var exact = Best(rows, r =>
                               {
                                   var text = Text(r);
                                   var compactText = text.Replace("-", string.Empty).Replace(" ", string.Empty);

                                   return Eq(r.SheetName, "Outdoor Price Book") &&
                                          text.Contains("safety", StringComparison.OrdinalIgnoreCase) &&
                                          text.Contains("screen", StringComparison.OrdinalIgnoreCase) &&
                                          (text.Contains(part, StringComparison.OrdinalIgnoreCase) ||
                                           text.Contains(sku, StringComparison.OrdinalIgnoreCase) ||
                                           compactText.Contains(compactPart, StringComparison.OrdinalIgnoreCase) ||
                                           compactText.Contains(sku, StringComparison.OrdinalIgnoreCase));
                               });

        if (exact is not null)
        {
            exact.Description = OutdoorVentFreeScreenDescription(type, model);
            return exact;
        }

        var fallback = Best(rows, r => Eq(r.SheetName, "Outdoor Price Book") &&
                                       Text(r).Contains("safety", StringComparison.OrdinalIgnoreCase) &&
                                       Text(r).Contains("screen", StringComparison.OrdinalIgnoreCase) &&
                                       ContainsSize(r, sizeNum) && ContainsAll(Text(r), StyleWords(type, model)) &&
                                       (string.IsNullOrWhiteSpace(glassNum) || ContainsGlass(r, glassNum)));

        if (fallback is not null)
            fallback.Description = OutdoorVentFreeScreenDescription(type, model);

        return fallback;
    }

    private static bool IsSeeThroughForReflectiveBackRules(FireplaceType type, string? model)
    {
        if (type is FireplaceType.IndoorSeeThrough or FireplaceType.IndoorOutdoorSeeThrough or
                FireplaceType.OutdoorSeeThrough)
            return true;

        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var text = Regex.Replace(model ?? string.Empty, @"[\-_]+", " ").ToLowerInvariant();

        return compact.StartsWith("ST") || compact.StartsWith("VST") || compact.StartsWith("VFST") ||
               text.Contains("see through") || text.Contains("see-through");
    }
    private static PriceRow? FindReflectiveBackRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                                   string sizeNum, string glassHeight)
    {
        // See Through fireplaces do not have a back; use Reflective Sides instead where applicable.
        if (IsSeeThroughForReflectiveBackRules(type, model))
            return null;
        var sheet = type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough ? "Outdoor Price Book"
                                                                                     : "Indoor Price Book";
        var style = StyleCode(type, model);
        var glassSuffix = GlassSuffix(glassHeight);
        var prefix = type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough ? "VF-RB" : "RB";
        var exact = string.IsNullOrWhiteSpace(glassSuffix) ? $"{prefix}-{style}-{sizeNum}"
                                                           : $"{prefix}-{style}-{sizeNum}-{glassSuffix}";

        var exactNoDash = exact.Replace("-", string.Empty);
        var bySku = Best(
            rows, r => Eq(r.SheetName, sheet) &&
                       (Text(r).Contains(exact, StringComparison.OrdinalIgnoreCase) ||
                        Text(r).Replace("-", string.Empty).Contains(exactNoDash, StringComparison.OrdinalIgnoreCase)) &&
                       Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                       Text(r).Contains("back", StringComparison.OrdinalIgnoreCase));
        if (bySku is not null)
            return bySku;

        return Best(rows, r => Eq(r.SheetName, sheet) &&
                               Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("back", StringComparison.OrdinalIgnoreCase) &&
                               ContainsAll(Text(r), StyleWords(type, model)) && ContainsSize(r, sizeNum) &&
                               (string.IsNullOrWhiteSpace(GlassInches(glassHeight)) ||
                                ContainsGlass(r, GlassInches(glassHeight))));
    }

    private static PriceRow? FindReflectiveSidesRow(IReadOnlyList<PriceRow> rows, FireplaceType type, string model,
                                                    string glassHeight)
    {
        var sheet = type == FireplaceType.Large                                        ? "Large Price Book"
                    : type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough ? "Outdoor Price Book"
                                                                                       : "Indoor Price Book";
        var style = StyleCode(type, model);
        var suffix = GlassSuffix(glassHeight);
        var isOutdoor = type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough;
        var prefix = isOutdoor ? "VF-RBS" : type == FireplaceType.Large ? "FLARE-RBS" : "RBS";
        var styleForSku = type == FireplaceType.Large ? LargeReflectiveSidesStyleCode(style) : style;
        var exactCandidates = new List<string>();
        var exact = string.IsNullOrWhiteSpace(suffix) ? $"{prefix}-{styleForSku}" : $"{prefix}-{styleForSku}-{suffix}";
        exactCandidates.Add(exact);

        // Regular 16-inch reflective-side SKUs omit the R suffix for all vent-free models
        // and for indoor FF/ST models (for example VF-RBS-ST and RBS-ST).
        if (suffix.Equals("R", StringComparison.OrdinalIgnoreCase) &&
            (isOutdoor || styleForSku is "FF" or "ST"))
            exactCandidates.Insert(0, $"{prefix}-{styleForSku}");

        var compactCandidates = exactCandidates.Select(candidate => candidate.Replace("-", string.Empty)).ToList();
        var bySku = Best(
            rows, r => Eq(r.SheetName, sheet) && Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                       Text(r).Contains("side", StringComparison.OrdinalIgnoreCase) &&
                       (exactCandidates.Any(candidate => Text(r).Contains(candidate, StringComparison.OrdinalIgnoreCase)) ||
                        compactCandidates.Any(candidate =>
                                                  Text(r).Replace("-", string.Empty)
                                                      .Contains(candidate, StringComparison.OrdinalIgnoreCase))));
        if (bySku is not null)
            return bySku;

        return Best(rows, r => Eq(r.SheetName, sheet) &&
                               Text(r).Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                               Text(r).Contains("side", StringComparison.OrdinalIgnoreCase) &&
                               ContainsAll(Text(r), StyleWords(type, model)) &&
                               (string.IsNullOrWhiteSpace(GlassInches(glassHeight)) ||
                                ContainsGlass(r, GlassInches(glassHeight))));
    }

    private static string LargeReflectiveSidesStyleCode(string styleCode) => styleCode switch { "LC" => "LC",
                                                                                                "RC" => "RC",
                                                                                                _ => "FF" };

    private static string RecommendedHeatReleaseLouverSize(FireplaceType type, string sizeNum, string model)
    {
        var normalizedModel = Normalize(model);
        if (type == FireplaceType.Traditional || normalizedModel.Contains("traditional"))
            return "100";
        if (!int.TryParse(sizeNum, out var sizeInt))
            sizeInt = 0;
        if (normalizedModel.Contains("passage") || sizeInt <= 30)
            return "70";
        if (sizeInt is 42 or 45 or 46 or 50)
            return "100";
        if (sizeInt is 60 or 70)
            return "120";
        if (sizeInt == 80)
            return "180";
        if (sizeInt >= 100)
            return "200";
        return "100";
    }

    private static string RecommendedAirIntakeLouverSize(FireplaceType type, string sizeNum, string model)
    {
        var normalizedModel = Normalize(model);
        if (type == FireplaceType.Traditional || normalizedModel.Contains("traditional"))
            return "70";
        if (!int.TryParse(sizeNum, out var sizeInt))
            sizeInt = 0;
        if (normalizedModel.Contains("passage") || sizeInt <= 30)
            return "50";
        if (sizeInt is 42 or 45 or 46 or 50)
            return "50";
        if (sizeInt is 60 or 70)
            return "70";
        return "100";
    }

    private static string RecommendedLouverSize(FireplaceType type, string sizeNum, string model)
    {
        var normalizedModel = Normalize(model);
        if (type == FireplaceType.Traditional || normalizedModel.Contains("traditional"))
            return "100";

        if (!int.TryParse(sizeNum, out var sizeInt))
            sizeInt = 0;

        if (normalizedModel.Contains("passage") || sizeInt <= 30)
            return "70";
        if (sizeInt is 42 or 45 or 46 or 50)
            return "100";
        if (sizeInt is 60 or 70)
            return "120";
        if (sizeInt is 80 or 100)
            return "180";
        if (sizeInt >= 200)
            return "200";
        if (sizeInt >= 180)
            return "180";
        if (sizeInt >= 140)
            return "140";
        if (sizeInt >= 120)
            return "120";
        return "100";
    }

    private static PriceRow? FindLouverRowBySize(PriceBookWorkbook wb, string size)
    {
        size = (size ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(size))
            return null;

        var rows = wb.Rows.Where(r => Eq(r.SheetName, "Parts Price Book") && r.Price is not null).ToList();
        var exactPart = $"LUV-{size}";
        var exactSku = $"LUV{size}";

        var exact = Best(rows, r => Eq((r.PartName ?? string.Empty).Replace(" ", string.Empty), exactPart) ||
                                    Eq((r.Sku ?? string.Empty).Replace(" ", string.Empty), exactSku));
        if (exact is not null)
            return exact;

        var byDescription = Best(rows, r => Text(r).Contains("louver", StringComparison.OrdinalIgnoreCase) &&
                                            Regex.IsMatch(Text(r), $@"(?<!\d){Regex.Escape(size)}(?!\d)"));
        if (byDescription is not null)
            return byDescription;

        if (LouverMsrpFallback.TryGetValue(size, out var price))
        {
            return new PriceRow { SheetName = "Parts Price Book",
                                  Sku = exactSku,
                                  PartName = exactPart,
                                  Description = $"{size} SQ\" Louver",
                                  Price = price,
                                  RawValues = { ["Part Name"] = exactPart, ["Description"] = $"{size} SQ\" Louver",
                                                ["SKU"] = exactSku,
                                                ["MSRP"] = price.ToString(CultureInfo.InvariantCulture) } };
        }

        return null;
    }

    private static PriceRow? FindPremiumMediaRow(PriceBookWorkbook wb, string key, string display)
    {
        var rows = wb.Rows.Where(r => r.Price is not null).ToList();
        var norm = Normalize($"{key} {display}");

        PriceRow ? BySku(params string[] skus) =>
                       Best(rows, r => skus.Any(sku => Text(r).Contains(sku, StringComparison.OrdinalIgnoreCase)));

        if (norm.Contains("black fire glass"))
            return BySku("CMDFGBL", "MEDIA-FG-Black");
        if (norm.Contains("reflective silver") || norm.Contains("silver"))
            return BySku("CMDFGS", "MEDIA-FG-Silver");
        if (norm.Contains("bronze"))
            return BySku("CMDFGBR", "MEDIA-FG-Bronze");
        if (norm.Contains("ice"))
            return BySku("CMDFGIC", "MEDIA-FG-Ice");
        if (norm.Contains("clear") && norm.Contains("glass"))
            return BySku("CMDFGCL", "MEDIA-FG-Clear");
        if (norm.Contains("black diamond") || norm.Contains("zircon"))
            return BySku("CMDPDB", "MEDIA-PD-Black");
        if (norm.Contains("rain"))
            return BySku("CMDPDR", "MEDIA-PD-Rain");
        if (norm.Contains("white") && norm.Contains("pebble"))
            return BySku("CMDCPW", "MEDIA-CP-White");
        if (norm.Contains("black") && norm.Contains("pebble"))
            return BySku("CMDCPB", "MEDIA-CP-Black");
        if (norm.Contains("earth") && norm.Contains("pebble"))
            return BySku("CMDCPE", "MEDIA-CP-Earth");
        if (norm.Contains("white") && norm.Contains("log"))
            return BySku("CMDGLW", "MEDIA-GL-White");
        if (norm.Contains("ceramic logs") || norm.Contains("wood logs"))
            return BySku("CMDGLWO", "MEDIA-GL-Wood");
        if (norm.Contains("branch"))
            return BySku("CMDGLB", "MEDIA-GL-Branches");
        if (norm.Contains("black") && norm.Contains("ember"))
            return BySku("CMDGLEB", "MEDIA-GL-Embers-Bla");
        if (norm.Contains("ember"))
            return BySku("CMDGLE", "MEDIA-GL-Embers");
        if (norm.Contains("tr42bch") || norm.Contains("tr42 birch") ||
            (norm.Contains("traditional 42") && norm.Contains("birch")))
            return BySku("TR42BCH", "TR42-BIRCH");

        if (norm.Contains("tr46bch") || norm.Contains("tr46 birch") ||
            (norm.Contains("traditional 46") && norm.Contains("birch")))
            return BySku("TR46BCH", "TR46-BIRCH");

        if (norm.Contains("pmdbirch") || norm.Contains("pmd birch") || norm.Contains("traditional birchwood"))
            return BySku("PMDBIRCH", "PSA-Birch");

        if (norm.Contains("loak42") || (norm.Contains("large oak") && norm.Contains("42")))
            return BySku("LOAK42", "LRG-OAK-42");

        if (norm.Contains("loak46") || (norm.Contains("large oak") && norm.Contains("46")))
            return BySku("LOAK46", "LRG-OAK-46");

        if (norm.Contains("gold") && norm.Contains("glass"))
            return Best(rows, r => Text(r).Contains("Gold", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Glass", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("aqua") && norm.Contains("glass"))
            return Best(rows, r => Text(r).Contains("Aqua", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Glass", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("black stones") || norm.Contains("premium black"))
            return Best(rows, r => Text(r).Contains("Premium Black", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("white stones") || norm.Contains("cottage white stones"))
            return Best(rows, r => Text(r).Contains("Cottage White", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Stones", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("birch"))
            return Best(rows, r => Text(r).Contains("Birchwood", StringComparison.OrdinalIgnoreCase) ||
                                   Text(r).Contains("PMDBIRCH", StringComparison.OrdinalIgnoreCase) ||
                                   Text(r).Contains("PSA-Birch", StringComparison.OrdinalIgnoreCase));
        if (norm.Equals("driftwood", StringComparison.OrdinalIgnoreCase) || norm.Contains("premium driftwood"))
            return Best(rows, r => Text(r).Contains("Large Drift", StringComparison.OrdinalIgnoreCase)) ??
                   Best(rows, r => Text(r).Contains("Small Drift", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("small drift"))
            return Best(rows, r => Text(r).Contains("Small Drift", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("large drift"))
            return Best(rows, r => Text(r).Contains("Large Drift", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("grey") && norm.Contains("2"))
            return Best(rows, r => Text(r).Contains("Cape Grey", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("2\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("grey") && norm.Contains("4"))
            return Best(rows, r => Text(r).Contains("Cape Grey", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("4\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("grey"))
            return Best(rows, r => Text(r).Contains("Cape Grey", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Mixed", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("black") && norm.Contains("2"))
            return Best(rows, r => Text(r).Contains("Matte Black", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("2\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("black") && norm.Contains("4"))
            return Best(rows, r => Text(r).Contains("Matte Black", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("4\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("black"))
            return Best(rows, r => Text(r).Contains("Matte Black", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Mixed", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("white") && norm.Contains("2"))
            return Best(rows, r => Text(r).Contains("Cottage White", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("2\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("white") && norm.Contains("4"))
            return Best(rows, r => Text(r).Contains("Cottage White", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("4\"", StringComparison.OrdinalIgnoreCase));
        if (norm.Contains("white"))
            return Best(rows, r => Text(r).Contains("Cottage White", StringComparison.OrdinalIgnoreCase) &&
                                   Text(r).Contains("Mixed", StringComparison.OrdinalIgnoreCase));
        return Best(rows, r => Text(r).Contains(display, StringComparison.OrdinalIgnoreCase));
    }
    private static bool IsOutdoorVentFreeResourceModel(string? model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return Regex.IsMatch(compact, @"^(VFF|VST|VLC|VRC|VDC|VFST|VFLC|VFRC|VFDC)\d{2,3}(R|H|EH)?$") ||
               compact.StartsWith("VFF", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VST", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VLC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VRC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VDC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VFST", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VFLC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VFRC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("VFDC", StringComparison.OrdinalIgnoreCase);
    }

    private static string GlassSuffixFromModelCode(string? model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        if (compact.EndsWith("EH", StringComparison.OrdinalIgnoreCase))
            return "EH";

        if (compact.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            return "H";

        if (compact.EndsWith("R", StringComparison.OrdinalIgnoreCase))
            return "R";

        return string.Empty;
    }

    private static PriceRow? TryGetOutdoorVentFreeResourceRowFromWorkbook(FireplaceType type, string model, string size,
                                                                          string glassHeight)
    {
        if (type is not(FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough) &&
            !IsOutdoorVentFreeResourceModel(model))
            return null;

        try
        {
            var root = AppContext.BaseDirectory;

            var candidates = new[] {
                Path.Combine(root, "LocalData", "outdoor_spec_center_extracted_links.xlsx"),
                Path.Combine(root, "..", "..", "..", "..", "LocalData", "outdoor_spec_center_extracted_links.xlsx"),
                Path.Combine(Environment.CurrentDirectory, "LocalData", "outdoor_spec_center_extracted_links.xlsx"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop",
                             "Quote Request", "LocalData", "outdoor_spec_center_extracted_links.xlsx"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop",
                             "Flare Quotes", "LocalData", "outdoor_spec_center_extracted_links.xlsx")
            };

            var workbookPath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

            if (string.IsNullOrWhiteSpace(workbookPath))
                return null;

            using var workbook = new XLWorkbook(workbookPath);

            var sheet = workbook.Worksheets.FirstOrDefault(
                ws => ws.Name.Equals("App Resource Rows", StringComparison.OrdinalIgnoreCase));

            sheet ??= workbook.Worksheets.FirstOrDefault();

            if (sheet is null)
                return null;

            var desiredKeys = OutdoorVentFreeResourceKeys(type, model, size, glassHeight)
                                  .Select(NormalizeOutdoorResourceKey)
                                  .Where(x => !string.IsNullOrWhiteSpace(x))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

            if (desiredKeys.Count == 0)
                return null;

            var requestedSuffix = GlassSuffix(glassHeight);
            var preferredKeys = requestedSuffix.Equals("R", StringComparison.OrdinalIgnoreCase)
                                    // The vent-free resource workbook omits the R suffix for 16-inch regular models.
                                    // Prefer VST70/VFST70 over synthetic VST70R/VFST70R keys.
                                    ? desiredKeys.Where(k => !Regex.IsMatch(k, @"(?:EH|H|R)$",
                                                                            RegexOptions.IgnoreCase))
                                                 .ToList()
                                    : desiredKeys
                                      .Where(k => string.IsNullOrWhiteSpace(requestedSuffix) ||
                                                  k.EndsWith(requestedSuffix,
                                                             StringComparison.OrdinalIgnoreCase))
                                      .ToList();

            if (preferredKeys.Count == 0)
                preferredKeys = desiredKeys;

            var usedRows = sheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
                return null;

            var headerRow = usedRows.FirstOrDefault(
                row =>
                {
                    var joined = string.Join(" ", row.CellsUsed().Select(c => c.GetString()));
                    return joined.Contains("Model Key", StringComparison.OrdinalIgnoreCase) ||
                           joined.Contains("Product Sheet", StringComparison.OrdinalIgnoreCase) ||
                           joined.Contains("Framing Guide", StringComparison.OrdinalIgnoreCase);
                });

            var hasHeader = headerRow is not null;
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (hasHeader)
            {
                foreach (var cell in headerRow!.CellsUsed())
                {
                    var header = (cell.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
                        headers[header] = cell.Address.ColumnNumber;
                }
            }

            int Col(int fallback, params string[] names)
            {
                if (hasHeader)
                {
                    foreach (var name in names)
                    {
                        if (headers.TryGetValue(name, out var col))
                            return col;
                    }
                }

                return fallback;
            }

            var modelCol = Col(1, "Model Key", "Model #", "Model Number", "Model");
            var compactCol = Col(2, "Compact Key", "Compact", "SKU", "Part #");
            var productCol = Col(7, "Product Sheet", "Product", "Product Spec");
            var framingCol = Col(8, "Framing Guide", "Framing", "Wood Framing");
            var dimensionCol = Col(9, "Dimension File", "Dimensions", "Dimension");
            var cadCol = Col(10, "CAD", "CAD File");
            var sketchupCol = Col(11, "SketchUp", "Sketch Up");
            var revitCol = Col(12, "Revit", "Revit File");
            var fallbackCol = Col(13, "Fallback URL", "Fallback", "Download Center", "Download Center URL");

            var dataRows = hasHeader ? usedRows.Where(row => row.RowNumber() > headerRow!.RowNumber()) : usedRows;

            foreach (var row in dataRows)
            {
                string Cell(int col)
                {
                    if (col <= 0)
                        return string.Empty;

                    return (row.Cell(col).GetString() ?? string.Empty).Trim();
                }

                var modelKey = Cell(modelCol);
                var compactKey = Cell(compactCol);
                var productUrl = Cell(productCol);

                if (string.IsNullOrWhiteSpace(modelKey) ||
                    modelKey.Contains("note", StringComparison.OrdinalIgnoreCase) ||
                    modelKey.Contains("extraction", StringComparison.OrdinalIgnoreCase) ||
                    !productUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rowKeys = new[] { NormalizeOutdoorResourceKey(modelKey), NormalizeOutdoorResourceKey(compactKey) }
                                  .Where(k => !string.IsNullOrWhiteSpace(k))
                                  .ToList();

                if (!rowKeys.Any(k => preferredKeys.Contains(k, StringComparer.OrdinalIgnoreCase)))
                    continue;

                var normalizedModel = FirstNonBlank(modelKey, BuildModelNumber(type, model, size, glassHeight));
                var fallback = FirstNonBlank(Cell(fallbackCol), FallbackUrl(type));

                var priceRow =
                    new PriceRow { SheetName = "Resource Links", Sku = normalizedModel, PartName = normalizedModel,
                                   Description = "Outdoor Vent Free Resource Links" };

                priceRow.RawValues["Template"] = "Outdoor";
                priceRow.RawValues["Model #"] = normalizedModel;
                priceRow.RawValues["Model Number"] = normalizedModel;
                priceRow.RawValues["Model"] = normalizedModel;
                priceRow.RawValues["Product Sheet"] = Cell(productCol);
                priceRow.RawValues["Framing Guide"] = Cell(framingCol);
                priceRow.RawValues["Dimension File"] = Cell(dimensionCol);
                priceRow.RawValues["CAD"] = Cell(cadCol);
                priceRow.RawValues["SketchUp"] = Cell(sketchupCol);
                priceRow.RawValues["Revit"] = Cell(revitCol);
                priceRow.RawValues["Fallback URL"] = fallback;

                return priceRow;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
    private static IEnumerable<string> OutdoorVentFreeResourceKeys(FireplaceType type, string model, string size,
                                                                   string glassHeight)
    {
        var digits = Digits(size);
        var suffix = GlassSuffix(glassHeight);
        // Outdoor resource size/glass recovery
        if (string.IsNullOrWhiteSpace(digits))
            digits = Digits(model);

        if (string.IsNullOrWhiteSpace(suffix))
            suffix = GlassSuffixFromModelCode(model);
        var style = VentFreeModelStyleCode(type, model);

        if (string.IsNullOrWhiteSpace(digits) || string.IsNullOrWhiteSpace(style))
            yield break;

        var skuStyle = style switch { "VST" => "VFST", "VLC" => "VFLC", "VRC" => "VFRC", "VDC" => "VFDC",
                                      _ => "VFFF" };

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            yield return $"{style}-{digits}-{suffix}";
            yield return $"{style}{digits}{suffix}";
            yield return $"{skuStyle}{digits}{suffix}";
        }

        yield return $"{style}-{digits}";
        yield return $"{style}{digits}";
        yield return $"{skuStyle}{digits}";
    }

    private static string NormalizeOutdoorResourceKey(string? value)
    {
        return Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
    }

    private static PriceRow? TryGetLargeResourceRow(FireplaceType type, string model, string size, string glassHeight)
    {
        // block invalid Large See Through large resource row
        if (IsInvalidLargeSeeThroughModel(model, size))
            return null;
        if (type != FireplaceType.Large)
            return null;

        var digits = Digits(size);
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        var style = LargeResourceStyleCode(model);
        if (string.IsNullOrWhiteSpace(style))
            return null;

        var suffix = LargeResourceGlassSuffix(model, glassHeight);
        var modelCompact = $"{style}{digits}{suffix}";
        var modelDash = string.IsNullOrWhiteSpace(suffix) ? $"{style}-{digits}" : $"{style}-{digits}-{suffix}";

        var folder = style;
        var root = $"https://flarefireplaces.com/wp-content/uploads/Data/{folder}/";
        var framingStyle = string.IsNullOrWhiteSpace(suffix) ? style : $"{style}{suffix}";
        var ventCount = LargeResourceVentCount(digits);

        var row = new PriceRow { SheetName = "Large Generated Resource Links", Sku = modelCompact, PartName = modelDash,
                                 Description = $"Large {modelDash} resource links" };

        row.RawValues["Model #"] = modelCompact;
        row.RawValues["Model"] = modelDash;
        row.RawValues["3-Part Spec"] = $"{root}ThreePartSpec{modelCompact}.docx";
        row.RawValues["Product Sheet"] = $"{root}specs-{modelCompact}.pdf";
        row.RawValues["Dimensions"] = $"{root}dimensions-{modelCompact}.pdf";
        row.RawValues["Dimension File"] = $"{root}dimensions-{modelCompact}.pdf";
        row.RawValues["Wood Framing"] = $"{root}framing-{framingStyle}-Large-{ventCount}-wood.pdf";
        row.RawValues["Metal Framing"] = $"{root}framing-{framingStyle}-Large-{ventCount}-metal.pdf";

        // Not currently displayed for Large emails, but keep these available if Large ResourceColumns expand later.
        row.RawValues["CAD"] = $"{root}CAD-{modelCompact}.DWG";
        row.RawValues["SketchUp"] = $"{root}{modelDash}.skp";

        return row;
    }

    private static string LargeResourceStyleCode(string model)
    {
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var normalized = Normalize(model ?? string.Empty);

        if (compact.StartsWith("ST") || normalized.Contains("see through") || normalized.Contains("see-through"))
            return "ST";
        if (compact.StartsWith("LC") || normalized.Contains("left corner"))
            return "LC";
        if (compact.StartsWith("RC") || normalized.Contains("right corner"))
            return "RC";
        if (compact.StartsWith("DC") || normalized.Contains("double corner"))
            return "DC";
        return "FF";
    }

    private static string LargeResourceGlassSuffix(string model, string glassHeight)
    {
        var effectiveHeight = EffectiveGlassHeight(glassHeight, model);
        var suffix = GlassSuffix(effectiveHeight);

        if (!string.IsNullOrWhiteSpace(suffix))
            return suffix.Equals("EH", StringComparison.OrdinalIgnoreCase) ? "H" : suffix;

        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        return Regex.IsMatch(compact, @"(?:FF|ST|LC|RC|DC)\d{2,3}H$") ? "H" : string.Empty;
    }

    private static int LargeResourceVentCount(string sizeDigits)
    {
        return sizeDigits switch { "180" or "210" or "240" or "300" => 3, "280" or "320" or "400" => 4,
                                   "340" => 5,
                                   _ => 2 };
    }
    private static PriceRow? FindResourceRow(PriceBookWorkbook wb, FireplaceType type, string model, string size,
                                             string glassHeight)
    {
        glassHeight = EffectiveGlassHeight(glassHeight, model);
        var rows = wb.Rows.Where(r => IsResourceLinksSheet(r.SheetName)).ToList();
        if (rows.Count == 0)
            return null;

        var template = ResourceTemplate(type);
        var scopedRows = rows.Where(r => ResourceRowMatchesRequestedType(r, type, template)).ToList();
        if (scopedRows.Count == 0)
            scopedRows = rows.Where(r => ResourceTemplateMatches(r, template)).ToList();
        if (scopedRows.Count == 0)
            scopedRows = rows;

        // Passage resource row scoping
        if (IsPassageModel(model))
        {
            var passageScopedRows = scopedRows.Where(IsPassageResourceRow).ToList();
            if (passageScopedRows.Count > 0)
                scopedRows = passageScopedRows;
        }
        else
        {
            var nonPassageScopedRows = scopedRows.Where(r => !IsPassageResourceRow(r)).ToList();
            if (nonPassageScopedRows.Count > 0)
                scopedRows = nonPassageScopedRows;
        }
        var candidates = ResourceModelCandidates(type, model, size, glassHeight)
                             .Select(NormalizeResourceModelKey)
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();

        // Preserve candidate priority. For Vent Free / Outdoor units, VFF/VST/VLC/VRC/VDC rows
        // must win over similarly-sized indoor FF/ST/LC/RC/DC rows.
        foreach (var candidate in candidates)
        {
            var exact = scopedRows.FirstOrDefault(r => Eq(NormalizeResourceModelKey(ResourceModelKey(r)), candidate));
            if (exact is not null)
                return exact;
        }

        var style = ResourceStyle(type, model);
        var sizeNum = Digits(size);
        var glassNum = GlassInches(glassHeight);

        var styleSizeMatches = scopedRows.Where(r => ContainsAll(Text(r), style) && ContainsSize(r, sizeNum)).ToList();

        if (type != FireplaceType.Traditional && !string.IsNullOrWhiteSpace(glassNum))
        {
            var glassSpecific = styleSizeMatches.FirstOrDefault(
                r => Eq(First(r.RawValues, "Glass Height", "Glass", "Glass Ht"), glassNum) ||
                     ContainsGlass(r, glassNum) ||
                     NormalizeResourceModelKey(ResourceModelKey(r))
                         .EndsWith(GlassSuffix(glassHeight), StringComparison.OrdinalIgnoreCase));
            if (glassSpecific is not null)
                return glassSpecific;
        }

        return styleSizeMatches.FirstOrDefault();
    }

    private static string? ResolveModelNumber(PriceBookWorkbook wb, FireplaceType type, string model, string size,
                                              string glassHeight) =>
        First(FindResourceRow(wb, type, model, size, glassHeight)?.RawValues ?? new Dictionary<string, string>(),
              "Model #", "Model Number", "Model");

    private static bool IsResourceLinksSheet(string sheetName) =>
        sheetName.Contains("Resource", StringComparison.OrdinalIgnoreCase) &&
        sheetName.Contains("Link", StringComparison.OrdinalIgnoreCase);

    private static string ResourceTemplate(FireplaceType type) => type switch {
        FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough or FireplaceType.IndoorOutdoorSeeThrough => "Outdoor",
        FireplaceType.Traditional => "Traditional", FireplaceType.Large => "Large",
        _ => "Indoor"
    };

    private static bool ResourceTemplateMatches(PriceRow row, string template)
    {
        var rowTemplate = First(row.RawValues, "Template", "Category", "Fireplace Type", "Type");
        if (string.IsNullOrWhiteSpace(rowTemplate))
            return true;

        if (Eq(rowTemplate, template))
            return true;

        var normalizedRow = Normalize(rowTemplate);
        var normalizedTemplate = Normalize(template);
        return normalizedRow.Contains(normalizedTemplate, StringComparison.OrdinalIgnoreCase) ||
               normalizedTemplate.Contains(normalizedRow, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ResourceRowMatchesRequestedType(PriceRow row, FireplaceType type, string template)
    {
        var modelKey = NormalizeResourceModelKey(ResourceModelKey(row));
        var rowTemplate = Normalize(First(row.RawValues, "Template", "Category", "Fireplace Type", "Type"));
        var rowText = Normalize($"{ResourceModelKey(row)} {Text(row)} {rowTemplate}");

        var isVentFreeOutdoorKey = modelKey.StartsWith("VFF", StringComparison.OrdinalIgnoreCase) ||
                                   modelKey.StartsWith("VST", StringComparison.OrdinalIgnoreCase) ||
                                   modelKey.StartsWith("VLC", StringComparison.OrdinalIgnoreCase) ||
                                   modelKey.StartsWith("VRC", StringComparison.OrdinalIgnoreCase) ||
                                   modelKey.StartsWith("VDC", StringComparison.OrdinalIgnoreCase) ||
                                   modelKey.StartsWith("VF", StringComparison.OrdinalIgnoreCase);

        var isOutdoorSuffixKey = modelKey.EndsWith("OD", StringComparison.OrdinalIgnoreCase);
        var saysOutdoor = isVentFreeOutdoorKey || isOutdoorSuffixKey ||
                          rowTemplate.Contains("outdoor", StringComparison.OrdinalIgnoreCase) ||
                          rowText.Contains("outdoor", StringComparison.OrdinalIgnoreCase) ||
                          rowText.Contains("vent free", StringComparison.OrdinalIgnoreCase);
        var saysIndoor = rowTemplate.Contains("indoor", StringComparison.OrdinalIgnoreCase) ||
                         rowText.Contains("indoor", StringComparison.OrdinalIgnoreCase);
        var saysTraditional = rowTemplate.Contains("traditional", StringComparison.OrdinalIgnoreCase) ||
                              rowText.Contains("traditional", StringComparison.OrdinalIgnoreCase) ||
                              modelKey.Contains("TRA", StringComparison.OrdinalIgnoreCase);
        var saysLarge = rowTemplate.Contains("large", StringComparison.OrdinalIgnoreCase) ||
                        rowText.Contains("large", StringComparison.OrdinalIgnoreCase);

        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
            return isVentFreeOutdoorKey || (saysOutdoor && !saysIndoor);

        if (type == FireplaceType.IndoorOutdoorSeeThrough)
            return isOutdoorSuffixKey || rowText.Contains("indoor outdoor", StringComparison.OrdinalIgnoreCase);

        if (type == FireplaceType.Traditional)
            return saysTraditional || ResourceTemplateMatches(row, template);

        if (type == FireplaceType.Large)
            return saysLarge || ResourceTemplateMatches(row, template);

        // Indoor rows should not accidentally consume Vent Free / Outdoor rows.
        return !isVentFreeOutdoorKey && !isOutdoorSuffixKey && !saysOutdoor && ResourceTemplateMatches(row, template);
    }
    private static IEnumerable<string> ResourceModelCandidates(FireplaceType type, string model, string size,
                                                               string glassHeight)
    {
        var digits = Digits(size);
        var suffix = GlassSuffix(glassHeight);

        if (IsPassageModel(model) && string.IsNullOrWhiteSpace(digits))
            digits = "30";
        // Passage resource candidates
        if (IsPassageModel(model))
        {
            yield return PassageModelCode(model);
            yield return IsSeeThroughPassageModel(model) ? "PASSST" : "PASSFF";
        }

        if (string.IsNullOrWhiteSpace(digits))
            yield break;

        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            var outdoorStyle = VentFreeModelStyleCode(type, model);

            if (!string.IsNullOrWhiteSpace(suffix))
                yield return $"{outdoorStyle}-{digits}-{suffix}";

            yield return $"{outdoorStyle}-{digits}";

            if (!string.IsNullOrWhiteSpace(suffix))
                yield return $"{outdoorStyle}{digits}{suffix}";

            yield return $"{outdoorStyle}{digits}";

            // Lower-priority legacy fallbacks only after true Outdoor Vent Free keys.
            var indoorStyle = StyleCode(type, model ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(suffix))
                yield return $"{indoorStyle}-{digits}-{suffix}-OD";

            yield return $"{indoorStyle}-{digits}-OD";

            yield break;
        }

        if (IsPassageModel(model))
        {
            var style = PassageStyleCode(model);

            yield return $"{style}PASS";
            yield return $"PASS{style}";
            yield return $"{style}-PASS";
            yield return $"PASS-{style}";
            yield return $"{style}30EH";
            yield return $"{style}-30-EH";
            yield return $"{style}-30";
            yield return $"{style}30";
        }

        var built = BuildModelNumber(type, model, size, glassHeight);
        if (!string.IsNullOrWhiteSpace(built))
            yield return built;

        if (type == FireplaceType.Traditional)
        {
            yield return $"TR-{digits}";
            yield return $"TRA-{digits}";
            yield return $"DVTRA{digits}";
            yield return $"BONTR{digits}";
            yield return $"TRABON{digits}";
            yield break;
        }

        var styleCode = StyleCode(type, model);

        if (!string.IsNullOrWhiteSpace(suffix))
            yield return $"{styleCode}-{digits}-{suffix}";

        yield return $"{styleCode}-{digits}";

        if (type == FireplaceType.IndoorOutdoorSeeThrough)
        {
            if (!string.IsNullOrWhiteSpace(suffix))
                yield return $"ST-{digits}-{suffix}-OD";

            yield return $"ST-{digits}-OD";
        }
    }
    private static string ResourceModelKey(PriceRow row) => First(row.RawValues, "Model #", "Model Number", "Model",
                                                                  "SKU", "Part #", "Part Number");

    private static bool IsPassageResourceRow(PriceRow row)
    {
        var text =
            $"{ResourceModelKey(row)} {Text(row)} {First(row.RawValues, "Style", "Fireplace Style", "Description", "Template", "Category", "Type")}";
        var compact = Compact(text);

        return text.Contains("passage", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("FFPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("PASSFF", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("STPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("PASSST", StringComparison.OrdinalIgnoreCase);
    }
    private static string NormalizeResourceModelKey(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "^Flare-", string.Empty, RegexOptions.IgnoreCase).Trim();
        return Regex.Replace(cleaned, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
    }
    private static string ResourceUrl(PriceRow row, string label)
    {
        var requestedLabel = label ?? string.Empty;

        if (IsPassageResourceRow(row))
        {
            var text = $"{ResourceModelKey(row)} {Text(row)}";
            var compact = Compact(text);
            var requestedModel = First(row.RawValues, "Requested Model", "RequestedModel", "Input Model", "InputModel");
            var requestedCompact = Compact(requestedModel);
            var requestedNormalized = Normalize(requestedModel ?? string.Empty);

            var isOutdoorPassage = requestedCompact.Contains("OD", StringComparison.OrdinalIgnoreCase) ||
                                   requestedCompact.Contains("IO", StringComparison.OrdinalIgnoreCase) ||
                                   requestedNormalized.Contains("indoor outdoor", StringComparison.OrdinalIgnoreCase) ||
                                   requestedNormalized.Contains("indooroutdoor", StringComparison.OrdinalIgnoreCase) ||
                                   requestedNormalized.Contains("indoor/outdoor", StringComparison.OrdinalIgnoreCase) ||
                                   requestedNormalized.Contains("outdoor passage", StringComparison.OrdinalIgnoreCase);

            var isSeeThroughPassage = compact.Contains("STPASS", StringComparison.OrdinalIgnoreCase) ||
                                      compact.Contains("PASSST", StringComparison.OrdinalIgnoreCase) ||
                                      compact.Contains("ST30EH", StringComparison.OrdinalIgnoreCase) ||
                                      text.Contains("see through", StringComparison.OrdinalIgnoreCase) ||
                                      text.Contains("see-through", StringComparison.OrdinalIgnoreCase);

            if (requestedLabel.Equals("Wood Framing", StringComparison.OrdinalIgnoreCase) ||
                requestedLabel.Equals("Framing Guide", StringComparison.OrdinalIgnoreCase) ||
                requestedLabel.Equals("Framing", StringComparison.OrdinalIgnoreCase))
            {
                if (isOutdoorPassage && isSeeThroughPassage)
                    return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-ST-OD-wood.pdf";

                if (isSeeThroughPassage)
                    return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-ST-wood.pdf";

                return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-FF-wood.pdf";
            }

            if (requestedLabel.Equals("Metal Framing", StringComparison.OrdinalIgnoreCase))
            {
                if (isOutdoorPassage && isSeeThroughPassage)
                    return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-ST-OD-metal.pdf";

                if (isSeeThroughPassage)
                    return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-ST-metal.pdf";

                return "https://flarefireplaces.com/wp-content/uploads/Data/Passage/framing-PAS-FF-metal.pdf";
            }
        }

        var url = CleanCellUrl(First(row.RawValues, requestedLabel));
        if (!string.IsNullOrWhiteSpace(url))
            return url;

        if (requestedLabel == "Product Sheet")
            url = CleanCellUrl(First(row.RawValues, "Product", "Product Spec", "Product Specs"));
        if (requestedLabel == "Framing Guide")
            url = CleanCellUrl(First(row.RawValues, "Wood Framing", "Framing", "Framing Guide"));
        if (requestedLabel == "Dimension File")
            url = CleanCellUrl(First(row.RawValues, "Dimensions", "Dimension", "Dimension File"));
        if (requestedLabel == "3-Part Spec")
            url = CleanCellUrl(First(row.RawValues, "3 Part Spec", "Three Part Spec", "3-Part Spec"));

        return url;
    }
    private static string PriceLineUrl(PriceRow? row)
    {
        if (row is null)
            return string.Empty;

        return CleanCellUrl(First(row.RawValues, "URL", "Url", "Link", "Links", "Website", "Web URL", "Page URL",
                                  "Product URL", "Product Sheet", "Product Spec", "3-Part Spec", "Spec URL",
                                  "Specification URL", "Install Manual", "Manual URL"));
    }

    private static string CleanCellUrl(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   ? text
                   : string.Empty;
    }

    private static string[] ResourceColumns(FireplaceType type) => type switch {
        FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough or
            FireplaceType.IndoorOutdoorSeeThrough => ["Product Sheet", "Framing Guide", "Dimension File", "CAD",
                                                      "SketchUp", "Revit"],
        FireplaceType.Large => ["3-Part Spec", "Product Sheet", "Dimensions", "Wood Framing", "Metal Framing"],
        _ => ["3-Part Spec", "CAD", "SketchUp", "Revit", "Wood Framing", "Metal Framing"]
    };

    private static string FallbackUrl(FireplaceType type) =>
        type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough or FireplaceType.IndoorOutdoorSeeThrough
            ? "https://flarefireplaces.com/contact-flare/outdoor-fireplace-spcification-center/"
            : "https://flarefireplaces.com/contact-flare/architect-design-download/";

    private static string CanonicalFeatureName(string feature)
    {
        var f = Normalize(feature);

        if (f.Contains("summit burner"))
            return "Summit Burner";
        if (f.Contains("double glass"))
            return "Double Glass";
        if (f.Contains("reflective black back"))
            return "Reflective Black Back";
        if (f.Contains("reflective black sides"))
            return "Reflective Black Sides";
        if (f.Contains("rgb"))
            return "RGB LEDs";
        if (f.Contains("summer kit"))
            return "Summer Kit";
        if (f.Contains("active heat flex"))
            return "Active Heat Flex";
        if (f.Contains("passive heat flex"))
            return "Passive Heat Flex";
        if (f.Contains("heat release louver"))
            return "Heat Release Louver";
        if (f.Contains("air intake louver"))
            return "Air Intake Louver";
        if (f.Contains("power vent"))
            return "Power Vent";
        if (f.Contains("reflective black interior"))
            return "Reflective Black Interior";
        if (f.Contains("brick traditional"))
            return feature.Trim();

        return string.IsNullOrWhiteSpace(feature) ? string.Empty : feature.Trim();
    }

    private static string OverrideFeatureDescription(string feature, string desc)
    {
        var f = Normalize(feature);

        if (f.Contains("summit burner"))
            return "Driftwood In-Log Elevated Burner System";
        if (f.Contains("double glass"))
            return "Crystal Clear Double Glass Safety Barrier";
        if (f.Contains("reflective black back"))
            return "Black Glass Back That Reflects the Flame and Media";
        if (f.Contains("reflective black sides"))
            return "Black Glass Sides That Reflects the Flame and Media";
        if (f.Contains("rgb"))
            return "RGB Lighting Adds Playful Accent or Natural Glow Inside";
        if (f.Contains("summer kit"))
            return "Heat Removal Wired to a Switch";
        if (f.Contains("active heat flex"))
            return "Always Remove Heat to Remove The Heat Release";
        if (f.Contains("passive heat flex"))
            return "Keep it Simple Ducted Heat Release Register and Plenum";
        if (f.Contains("heat release louver"))
            return "Standardized Heat Release Register";
        if (f.Contains("air intake louver"))
            return "Standardized Air Intake Register";
        if (f.Contains("power vent"))
            return "Run Up to 8 90-Degree Elbows and 100' of Venting";
        if (f.Contains("reflective black interior"))
            return "Reflective Black Interior";

        return string.IsNullOrWhiteSpace(desc) ? feature : desc.Trim();
    }

    private static bool IsDriftwoodMedia(MediaSelection media)
    {
        var combined = Normalize(media.Key) + " " + Normalize(media.DisplayName);
        return combined.Contains("driftwood") || combined.Contains("drift wood") || combined.Contains("drift");
    }

    private static bool IsStoneBallsMedia(MediaSelection media)
    {
        var combined = Normalize($"{media.Key} {media.DisplayName}");
        return combined.Contains("stone balls") || combined.Contains("balls 2") || combined.Contains("balls 4") ||
               combined.Contains("psbgu") || combined.Contains("psbwu") || combined.Contains("psbbu") ||
               combined.Contains("psbgm") || combined.Contains("psbwm") || combined.Contains("psbbm");
    }

    private static PriceLine BuildStoneBallsPriceLine(MediaSelection media, string size)
    {
        var family = StoneBallsFamily(media);
        var color = StoneBallsColor(media);
        var sizeKey = StoneBallsSizeKey(size);
        var quantityText = StoneBallsQuantityText(family, sizeKey);
        var sku = StoneBallsSku(family, color, sizeKey);
        var price = StoneBallsMsrp(family, sizeKey);
        var displayName = StoneBallsDisplayName(family, color);
        var featureName = media.IsPremium ? displayName : $"Add. Classic Media - {displayName}";

        return new PriceLine {
            Feature = featureName, Description = $"{featureName} - {quantityText}", Sku = sku, Quantity = 1,
            Price = price,         SourceSheet = "Indoor Stone Balls Price Table"
        };
    }

    private static string StoneBallsFamily(MediaSelection media)
    {
        var combined = Normalize($"{media.Key} {media.DisplayName}");

        if (combined.Contains("mixed") || combined.Contains("2 4") || combined.Contains("psbgm") ||
            combined.Contains("psbwm") || combined.Contains("psbbm"))
            return "mixed";

        if (combined.Contains("4") || combined.Contains("u4") || combined.Contains("psbgu4") ||
            combined.Contains("psbwu4") || combined.Contains("psbbu4"))
            return "uniform4";

        return "uniform2";
    }

    private static string StoneBallsColor(MediaSelection media)
    {
        var combined = Normalize($"{media.Key} {media.DisplayName}");

        if (combined.Contains("white") || combined.Contains("cottage") || combined.Contains("psbwu") ||
            combined.Contains("psbwm"))
            return "white";

        if (combined.Contains("black") || combined.Contains("matte") || combined.Contains("psbbu") ||
            combined.Contains("psbbm"))
            return "black";

        return "grey";
    }

    private static string StoneBallsSizeKey(string size)
    {
        var digits = Digits(size);
        return digits switch { "25" => "25", "30" => "30", "45" => "45", "50" => "50",
                               "60" => "60", "70" => "70", "80" => "80", "100" => "100",
                               _ => "single" };
    }

    private static string StoneBallsDisplayName(string family, string color)
    {
        var colorText = color switch { "white" => "Cottage White", "black" => "Matte Black",
                                       _ => "Cape Grey" };

        return family switch { "uniform4" => $"Uniform 4\" {colorText} Stone Balls",
                               "mixed" => $"Mixed 2\" / 4\" {colorText} Stone Balls",
                               _ => $"Uniform 2\" {colorText} Stone Balls" };
    }

    private static string StoneBallsSku(string family, string color, string sizeKey)
    {
        var baseSku = (family, color) switch { ("uniform4", "white") => "PSBWU4",
                                               ("uniform4", "black") => "PSBBU4",
                                               ("uniform4", _) => "PSBGU4",
                                               ("mixed", "white") => "PSBWM",
                                               ("mixed", "black") => "PSBBM",
                                               ("mixed", _) => "PSBGM",
                                               (_, "white") => "PSBWU",
                                               (_, "black") => "PSBBU",
                                               _ => "PSBGU" };

        if (sizeKey == "single")
            return family == "uniform2" ? $"{baseSku}2" : baseSku;

        if (family == "uniform2")
            return $"{baseSku}2-{sizeKey}";

        return $"{baseSku}-{sizeKey}";
    }

    private static string StoneBallsQuantityText(string family, string sizeKey)
    {
        if (family == "mixed")
        {
            return sizeKey switch { "45" => "20-2\" / 8-4\" pcs",  "50" => "25-2\" / 9-4\" pcs",
                                    "60" => "27-2\" / 11-4\" pcs", "70" => "34-2\" / 14-4\" pcs",
                                    "80" => "36-2\" / 17-4\" pcs", "100" => "50-2\" / 20-4\" pcs",
                                    _ => "14-2\" / 6-4\" pcs" };
        }

        if (family == "uniform4")
        {
            return sizeKey switch { "25" or "30" => "16 pcs",
                                    "45" => "19 pcs",
                                    "50" => "28 pcs",
                                    "60" => "34 pcs",
                                    "70" => "35 pcs",
                                    "80" => "44 pcs",
                                    "100" => "56 pcs",
                                    _ => "11 pcs" };
        }

        return sizeKey switch { "25" or "30" => "20 pcs",
                                "45" => "25 pcs",
                                "50" => "28 pcs",
                                "60" => "35 pcs",
                                "70" => "40 pcs",
                                "80" => "45 pcs",
                                "100" => "56 pcs",
                                _ => "25 pcs" };
    }

    private static decimal StoneBallsMsrp(string family, string sizeKey)
    {
        if (family == "mixed")
        {
            return sizeKey switch { "45" => 257m, "50" => 322m, "60" => 355m, "70" => 449m, "80" => 484m, "100" => 643m,
                                    _ => 232m };
        }

        if (family == "uniform4")
        {
            return sizeKey switch { "25" or "30" => 215m,
                                    "45" => 269m,
                                    "50" => 392m,
                                    "60" => 473m,
                                    "70" => 489m,
                                    "80" => 607m,
                                    "100" => 783m,
                                    _ => 193m };
        }

        return sizeKey switch { "25" or "30" => 167m,
                                "45" => 209m,
                                "50" => 234m,
                                "60" => 292m,
                                "70" => 334m,
                                "80" => 376m,
                                "100" => 468m,
                                _ => 209m };
    }
    private static PriceLine BuildDriftwoodPriceLine(PriceBookWorkbook wb, FireplaceType type, string size,
                                                     string model)
    {
        var smallMedia =
            new MediaSelection { Key = "small_driftwood", DisplayName = "Small Driftwood", IsPremium = true };
        var largeMedia =
            new MediaSelection { Key = "large_driftwood", DisplayName = "Large Driftwood", IsPremium = true };

        var smallQty = CalculatePremiumMediaQuantityByGroup(wb, type, "drift_small", size, model);
        var largeQty = CalculatePremiumMediaQuantityByGroup(wb, type, "drift_large", size, model);

        // Fallback is intentionally deterministic and size/style-aware so Driftwood never silently prices as zero.
        // The workbook's Media Calculation sheets remain the source of truth when present.
        if (smallQty == 0 && largeQty == 0)
        {
            var fallback = DriftwoodFallbackQuantities(type, size, model);
            smallQty = fallback.Small;
            largeQty = fallback.Large;
        }

        var smallRow = smallQty > 0 ? FindPremiumMediaRow(wb, smallMedia.Key, smallMedia.DisplayName) : null;
        var largeRow = largeQty > 0 ? FindPremiumMediaRow(wb, largeMedia.Key, largeMedia.DisplayName) : null;

        decimal? total = 0m;
        if (smallQty > 0)
            total = smallRow?.Price is null ? null : total + smallRow.Price.Value * smallQty;
        if (largeQty > 0)
            total = total is null || largeRow?.Price is null ? null : total + largeRow.Price.Value * largeQty;

        var parts = new List<string>();
        if (smallQty > 0)
            parts.Add($"{smallQty} Small");
        if (largeQty > 0)
            parts.Add($"{largeQty} Large");

        return new PriceLine {
            Feature = "Driftwood",
            Description = parts.Count > 0 ? $"Driftwood Logs - {string.Join(" / ", parts)}" : "Driftwood Logs",
            Sku = string.Join(", ", new[] { smallRow?.Sku, largeRow?.Sku }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Quantity = Math.Max(1, smallQty + largeQty),
            Price = total,
            SourceSheet = FirstNonBlank(smallRow?.SheetName, largeRow?.SheetName)
        };
    }

    private static int CalculatePremiumMediaQuantityByGroup(PriceBookWorkbook wb, FireplaceType type, string group,
                                                            string size, string model)
    {
        type = ForceOutdoorVentFreeTypeFromModel(type, model);
        var sizeNum = Digits(size);
        if (string.IsNullOrWhiteSpace(sizeNum))
            return 0;

        if (type == FireplaceType.Large)
        {
            var large = FindLargeMediaQuantity(wb, group, sizeNum);
            if (large > 0)
                return large;
        }

        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            var outdoor = FindOutdoorMediaQuantity(wb, group, sizeNum);
            if (outdoor > 0)
                return outdoor;
        }

        var indoor = FindIndoorMediaQuantity(wb, group, sizeNum, StyleCode(type, model));
        if (indoor > 0)
            return indoor;

        if (group is "drift_small" or "drift_large")
        {
            var fallback = DriftwoodFallbackQuantities(type, size, model);
            return group == "drift_small" ? fallback.Small : fallback.Large;
        }

        return 0;
    }

    private static bool IsRoomDefinerReflectiveFeature(string feature)
    {
        var normalized = Normalize(feature);
        return normalized.Contains("reflective black back") || normalized.Contains("reflective back") ||
               normalized.Contains("reflective black sides") || normalized.Contains("reflective sides");
    }
    private static bool IsReflectiveBlackBackFeature(string feature)
    {
        return Normalize(feature).Contains("reflective black back") || Normalize(feature).Contains("reflective back");
    }
    private static bool IsDoubleCorner(string model, string size, string glassHeight)
    {
        var combined = Normalize($"{model} {size} {glassHeight}");
        var compact = Regex.Replace($"{model} {size} {glassHeight}", @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return combined.Contains("double corner") || compact.Equals("DC", StringComparison.OrdinalIgnoreCase) ||
               compact.StartsWith("DC", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("DOUBLECORNER", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("LDVDC", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("DVDC", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("VDC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReflectiveSidesFeature(string feature)
    {
        var normalized = Normalize(feature);
        return normalized.Contains("reflective") && (normalized.Contains("side") || normalized.Contains("sides"));
    }

    private static bool IsRoomDefiner(string model, string size, string glassHeight)
    {
        var combined = Normalize($"{model} {size} {glassHeight}");
        var modelOnly = Normalize(model);
        var compact = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return modelOnly.Equals("rd", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("RD", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("ROOMDEFINER", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("room definer") || combined.Contains("dvdrd") || combined.Contains(" rd ") ||
               combined.EndsWith(" rd") || combined.StartsWith("rd ");
    }

    private static int CalculatePremiumMediaQuantity(PriceBookWorkbook wb, FireplaceType type, MediaSelection media,
                                                     string size, string model)
    {
        type = ForceOutdoorVentFreeTypeFromModel(type, model);
        var group = MediaCalculationGroup(media);
        var sizeNum = Digits(size);
        var mediaKey = Normalize(media.Key);

        // Traditional log/media SKUs are sold as complete sets, not fireplace-length quantity calculations.
        if (type == FireplaceType.Traditional &&
            (mediaKey.Contains("tr42bch") || mediaKey.Contains("tr46bch") || mediaKey.Contains("pmd birch") ||
             mediaKey.Contains("pmdbirch") || mediaKey.Contains("loak42") || mediaKey.Contains("loak46")))
            return 1;

        if (string.IsNullOrWhiteSpace(sizeNum))
            return 1;

        if (type == FireplaceType.Large)
        {
            var large = FindLargeMediaQuantity(wb, group, sizeNum);
            if (large > 0)
                return large;
        }

        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            var outdoor = FindOutdoorMediaQuantity(wb, group, sizeNum);
            if (outdoor > 0)
                return outdoor;
        }

        var indoor = FindIndoorMediaQuantity(wb, group, sizeNum, StyleCode(type, model));
        if (indoor > 0)
            return indoor;

        return EstimatePremiumMediaQuantityFallback(media.Key, size, model);
    }

    private static string MediaCalculationGroup(MediaSelection media)
    {
        var key = Normalize(media.Key);
        var display = Normalize(media.DisplayName);
        var combined = key + " " + display;

        if (combined.Contains("gold") || combined.Contains("aqua") || combined.Contains("glass"))
            return "fireglass";
        if (combined.Contains("diamond") || combined.Contains("rain"))
            return "raindrop_diamonds";
        if (combined.Contains("black stones") || combined.Contains("white stones") ||
            combined.Contains("premium stones"))
            return "premium_stones";
        if (combined.Contains("grey balls 2") || combined.Contains("white balls 2") ||
            combined.Contains("black balls 2") || combined.Contains("u2"))
            return "stone_uniform_2";
        if (combined.Contains("grey balls 4") || combined.Contains("white balls 4") ||
            combined.Contains("black balls 4") || combined.Contains("u4"))
            return "stone_uniform_4";
        if (combined.Contains("mixed"))
            return "stone_mixed";
        if (combined.Contains("loak42") || combined.Contains("loak46") || combined.Contains("large oak"))
            return "traditional_oak";
        if (combined.Contains("tr42bch") || combined.Contains("tr46bch") || combined.Contains("pmdbirch") ||
            combined.Contains("pmd birch"))
            return "traditional_birch";
        if (combined.Contains("small drift"))
            return "drift_small";
        if (combined.Contains("large drift"))
            return "drift_large";
        if (combined.Contains("driftwood") || combined.Contains("drift wood"))
            return "driftwood";
        if (combined.Contains("birch"))
            return "birchwood";
        if (combined.Contains("ember"))
            return "embers";
        if (combined.Contains("branch"))
            return "branches";
        if (combined.Contains("pebble"))
            return "ceramic_pebbles";
        if (combined.Contains("white log"))
            return "white_logs";
        if (combined.Contains("log"))
            return "ceramic_logs";
        return key;
    }
    private static int FindLargeMediaQuantity(PriceBookWorkbook wb, string group, string sizeNum)
    {
        var rows = wb.Rows.Where(r => Eq(r.SheetName, "Large -Media Calculation")).ToList();
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(sizeNum))
            return 0;

        var sizeToken = $"flare-{sizeNum}";
        foreach (var row in rows.Where(
                     r => Normalize(First(r.RawValues, "Column1", "UNIT SIZE")).Contains(Normalize(sizeToken))))
        {
            var col2 = First(row.RawValues, "Column2", "FIRE GLASS");
            var col3 = First(row.RawValues, "Column3", "BLACK DIAMOND & RAIN DROPS");
            var col4 = First(row.RawValues, "Column4", "CERAMIC PEBBLES");
            var col5 = First(row.RawValues, "Column5", "WHITE LOGS");
            var col6 = First(row.RawValues, "Column6", "CERAMIC LOGS");
            var col7 = First(row.RawValues, "Column7", "STONES");

            // The Large media calculator has two blocks with repeated unit-size rows.
            // Block 1 columns: Fire Glass, Black Diamond/Rain Drops, Ceramic Pebbles, White Logs, Ceramic Logs, Stones.
            // Block 2 columns: Driftwood Sm/Lrg, Birchwood, Earth Pebbles, Branches, blank, Embers.
            var secondBlockProbe = string.Join(" ", col2, col3, col4, col5, col7);
            var secondBlock = secondBlockProbe.Contains("set", StringComparison.OrdinalIgnoreCase) ||
                              secondBlockProbe.Contains("sm", StringComparison.OrdinalIgnoreCase) ||
                              secondBlockProbe.Contains("lrg", StringComparison.OrdinalIgnoreCase) ||
                              secondBlockProbe.Contains("bag", StringComparison.OrdinalIgnoreCase);

            string value;

            if (group is "birchwood" or "embers" or "branches" or "drift_small" or "drift_large")
            {
                if (!secondBlock)
                    continue;

                value = group switch { "birchwood" => col3, "embers" => col7, "branches" => col5,
                                       "drift_small" or "drift_large" => col2,
                                       _ => string.Empty };

                if (group == "drift_small")
                {
                    var small = Regex.Match(value, @"(\d+)\s*Sm", RegexOptions.IgnoreCase);
                    if (small.Success && int.TryParse(small.Groups[1].Value, out var q))
                        return q;
                }

                if (group == "drift_large")
                {
                    var large = Regex.Match(value, @"(\d+)\s*Lrg", RegexOptions.IgnoreCase);
                    if (large.Success && int.TryParse(large.Groups[1].Value, out var q))
                        return q;
                }

                var secondQty = ParseMediaQuantityToSellableSets(group, value);
                if (secondQty > 0)
                    return secondQty;
            }
            else
            {
                if (secondBlock)
                    continue;

                value = group switch {
                    "fireglass" => col2,
                    "raindrop_diamonds" => col3,
                    "ceramic_pebbles" => col4,
                    "white_logs" => col5,
                    "ceramic_logs" => col6,
                    "premium_stones" or "stone_uniform_2" or "stone_uniform_4" or "stone_mixed" => col7,
                    _ => string.Empty
                };

                var firstQty = ParseMediaQuantityToSellableSets(group, value);
                if (firstQty > 0)
                    return firstQty;
            }
        }

        return 0;
    }

    private static int FindOutdoorMediaQuantity(PriceBookWorkbook wb, string group, string sizeNum)
    {
        if (string.IsNullOrWhiteSpace(sizeNum))
            return 0;

        var normalizedGroup = Normalize(group);

        // Outdoor media calculator currently covers glass media by pounds.
        // We only convert the glass/dimond pound values into 10 lb sellable sets.
        var wantsDiamonds = normalizedGroup.Contains("diamond") || normalizedGroup.Contains("rain");

        var wantsFireGlass = normalizedGroup.Contains("fireglass") || normalizedGroup.Contains("fire glass") ||
                             normalizedGroup.Contains("glass");

        if (!wantsDiamonds && !wantsFireGlass)
            return 0;

        var rows = wb.Rows.Where(r => Eq(r.SheetName, "Outdoor Media - Calculation")).ToList();

        foreach (var row in rows)
        {
            var modelSize = First(row.RawValues, "MODEL SIZE", "Model Size", "Column1");
            var normalizedModelSize = Normalize(modelSize);

            var exactSizeMatch =
                normalizedModelSize.Equals(Normalize($"FLARE-{sizeNum}"), StringComparison.OrdinalIgnoreCase) ||
                normalizedModelSize.Equals(Normalize($"FLARE {sizeNum}"), StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(modelSize ?? string.Empty, $@"(?<!\d){Regex.Escape(sizeNum)}(?!\d)");

            if (!exactSizeMatch)
                continue;

            var raw = row.RawValues;

            var poundsText = wantsDiamonds ? First(raw, "DIAMOND QUANTITIES", "Diamond Quantities", "Column3")
                                           : First(raw, "FIRE GLASS QUANTITIES", "Fire Glass Quantities", "Column2");

            var pounds = ParseLeadingInt(poundsText);

            if (pounds <= 0)
                continue;

            return PoundsToTenPoundSets(pounds);
        }

        return OutdoorGlassMediaSetsFallback(sizeNum);
    }
    private static int FindIndoorMediaQuantity(PriceBookWorkbook wb, string group, string sizeNum, string styleCode)
    {
        var useRoomDefinerBlock = string.Equals(styleCode, "RD", StringComparison.OrdinalIgnoreCase);
        var sheetName = useRoomDefinerBlock ? "Indoor - Media Calculation -RD" : "Indoor - Media Calculation";
        var rows = wb.Rows.Where(r => Eq(r.SheetName, sheetName)).ToList();

        // Older workbooks kept the Room Definer block inside Indoor - Media Calculation.
        // Newer workbooks have a dedicated Indoor - Media Calculation -RD sheet.
        // Support both so future workbook updates do not break the app.
        if (rows.Count == 0 && useRoomDefinerBlock)
            rows = wb.Rows.Where(r => Eq(r.SheetName, "Indoor - Media Calculation")).ToList();
        else if (rows.Count == 0)
            return 0;

        var blockTitle = IndoorBlockTitle(group);
        if (string.IsNullOrWhiteSpace(blockTitle))
            return 0;

        var activeBlock = string.Empty;
        var inRoomDefinerSection = rows.Any(r => Eq(r.SheetName, "Indoor - Media Calculation -RD"));

        foreach (var row in rows)
        {
            var text = Text(row);

            if (text.Contains("For Room Definers", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ROOM DEFINER", StringComparison.OrdinalIgnoreCase))
                inRoomDefinerSection = true;

            if (IsIndoorMediaBlockHeader(text))
                activeBlock = NormalizeIndoorBlockHeader(text);

            if (!string.Equals(activeBlock, blockTitle, StringComparison.OrdinalIgnoreCase))
                continue;

            if (useRoomDefinerBlock != inRoomDefinerSection && Eq(row.SheetName, "Indoor - Media Calculation"))
                continue;

            if (!ContainsSize(row, sizeNum))
                continue;

            var quantityText = First(row.RawValues, "QUANTITY", "Column2");
            if (string.IsNullOrWhiteSpace(quantityText))
                quantityText =
                    row.RawValues.Values.Skip(1).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

            if (group == "drift_small")
            {
                var small = Regex.Match(quantityText, @"(\d+)\s*SMALL", RegexOptions.IgnoreCase);
                if (small.Success && int.TryParse(small.Groups[1].Value, out var q))
                    return q;
            }
            if (group == "drift_large")
            {
                var large = Regex.Match(quantityText, @"(\d+)\s*LARGE", RegexOptions.IgnoreCase);
                if (large.Success && int.TryParse(large.Groups[1].Value, out var q))
                    return q;
            }
            if (group == "stone_mixed")
                return 1;
            if (group is "stone_uniform_2" or "stone_uniform_4")
                return 1;

            var quantity = ParseMediaQuantityToSellableSets(group, quantityText);
            if (quantity > 0)
                return quantity;
        }

        return 0;
    }

    private static int PoundsToTenPoundSets(int pounds)
    {
        if (pounds <= 0)
            return 0;

        return Math.Max(1, (int)Math.Ceiling(pounds / 10m));
    }

    private static bool IsGlassPoundMediaGroup(string group)
    {
        var normalized = Normalize(group);
        return normalized.Contains("fireglass") || normalized.Contains("fire glass") || normalized.Contains("glass") ||
               normalized.Contains("raindrop") || normalized.Contains("diamond");
    }

    private static int ParseMediaQuantityToSellableSets(string group, string quantityText)
    {
        var quantity = ParseLeadingInt(quantityText);
        if (quantity <= 0)
            return 0;

        if (IsGlassPoundMediaGroup(group) && (quantityText.Contains("lb", StringComparison.OrdinalIgnoreCase) ||
                                              quantityText.Contains("lbs", StringComparison.OrdinalIgnoreCase) ||
                                              quantityText.Contains("pound", StringComparison.OrdinalIgnoreCase)))
        {
            return PoundsToTenPoundSets(quantity);
        }

        return quantity;
    }

    private static int OutdoorGlassMediaSetsFallback(string sizeNum)
    {
        return sizeNum switch { "50" => PoundsToTenPoundSets(12),
                                "60" => PoundsToTenPoundSets(14),
                                "70" => PoundsToTenPoundSets(16),
                                "80" => PoundsToTenPoundSets(20),
                                "100" => PoundsToTenPoundSets(24),
                                _ => 0 };
    }
    private static string IndoorBlockTitle(string group) => group switch {
        "fireglass" => "fireglass",
        "raindrop_diamonds" => "raindrop and black diamonds",
        "ceramic_pebbles" => "ceramic pebbles",
        "premium_stones" => "premium stones",
        "white_logs" => "white logs",
        "ceramic_logs" => "ceramic logs",
        "birchwood" => "birchwood",
        "embers" => "embers",
        "branches" => "branches",
        "drift_small" or "drift_large" => "driftwood",
        "stone_uniform_2" => "stone balls 2 uniform",
        "stone_uniform_4" => "stone balls 4 uniform",
        "stone_mixed" => "stone balls mixed",
        _ => string.Empty
    };

    private static bool IsIndoorMediaBlockHeader(string text)
    {
        var n = Normalize(text);
        return n.Contains("fireglass") || n.Contains("raindrop and black diamonds") ||
               n.Contains("ceramic pebbles black white earth") || n.Contains("white logs") ||
               n.Contains("premium stones") || n.Contains("ceramic logs") || n == "birchwood" || n == "embers" ||
               n.Contains("branches small large") || n.Contains("driftwood small large") ||
               n.Contains("stone balls 2 uniform") || n.Contains("stone balls 4 uniform") ||
               n.Contains("stone balls 2 4 mized") || n.Contains("driftwood small large");
    }

    private static string NormalizeIndoorBlockHeader(string text)
    {
        var n = Normalize(text);
        if (n.Contains("fireglass"))
            return "fireglass";
        if (n.Contains("raindrop and black diamonds"))
            return "raindrop and black diamonds";
        if (n.Contains("ceramic pebbles black white earth"))
            return "ceramic pebbles";
        if (n.Contains("white logs"))
            return "white logs";
        if (n.Contains("premium stones"))
            return "premium stones";
        if (n.Contains("ceramic logs"))
            return "ceramic logs";
        if (n == "birchwood")
            return "birchwood";
        if (n == "embers")
            return "embers";
        if (n.Contains("branches small large"))
            return "branches";
        if (n.Contains("driftwood small large"))
            return "driftwood";
        if (n.Contains("stone balls 2 uniform"))
            return "stone balls 2 uniform";
        if (n.Contains("stone balls 4 uniform"))
            return "stone balls 4 uniform";
        if (n.Contains("stone balls 2 4 mized"))
            return "stone balls mixed";
        return n;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static (int Small, int Large) DriftwoodFallbackQuantities(FireplaceType type, string size, string model)
    {
        var sizeDigits = Digits(size);
        if (!int.TryParse(sizeDigits, out var sizeNumber))
            return (0, 1);

        var isRoomDefiner = string.Equals(StyleCode(type, model), "RD", StringComparison.OrdinalIgnoreCase) ||
                            Normalize(model).Contains("room definer") || Normalize(model).Contains("dvdrd");

        if (isRoomDefiner)
        {
            return sizeNumber switch {
                45 => (1, 1),
                50 => (2, 1),
                60 => (2, 1),
                70 => (2, 2),
                80 => (2, 2),
                100 => (2, 3),
                _ => (0, 1)
            };
        }

        return sizeNumber switch {
            30 => (1, 0),
            42 => (1, 0),
            45 => (0, 1),
            46 => (0, 1),
            50 => (2, 0),
            60 => (1, 1),
            70 => (2, 1),
            80 => (1, 2),
            100 => (2, 2),
            _ => (0, 1)
        };
    }

    private static int ParseLeadingInt(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+");
        return match.Success && int.TryParse(match.Value, out var q) ? Math.Max(q, 1) : 0;
    }

    private static int EstimatePremiumMediaQuantityFallback(string key, string size, string model)
    {
        var s = Digits(size);
        if (!int.TryParse(s, out var n))
            return 1;
        if (key.Contains("glass", StringComparison.OrdinalIgnoreCase))
            return n switch { <=
                                  30 => 10,
                              <=
                                  50 => 19,
                              <=
                                  60 => 22,
                              <=
                                  70 => 26,
                              <=
                                  80 => 31,
                              _ => 38 };
        if (key.Contains("stones", StringComparison.OrdinalIgnoreCase))
            return n switch { <=
                                  30 => 4,
                              <=
                                  50 => 7,
                              <=
                                  60 => 8,
                              <=
                                  80 => 10,
                              _ => 12 };
        if (key.Contains("balls", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (key.Contains("drift", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("birch", StringComparison.OrdinalIgnoreCase))
            return n switch { <=
                                  50 => 1,
                              <=
                                  70 => 2,
                              _ => 3 };
        return 1;
    }

    private static int LargeSummerKitCount(string size)
    {
        if (!int.TryParse(size, out var n))
            return 2;
        if (n is 120 or 140 or 160 or 200)
            return 2;
        if (n is 180 or 210 or 240 or 300)
            return 3;
        if (n is 280 or 320 or 400)
            return 4;
        if (n == 340)
            return 5;
        return n >= 300 ? 4 : 2;
    }

    private static string BuildLabel(FireplaceQuote input)
    {
        var parts = new[] { input.FireplaceLocation, input.Model,
                            string.IsNullOrWhiteSpace(input.Size) ? string.Empty : $"{Digits(input.Size)}\"",
                            string.IsNullOrWhiteSpace(input.GlassHeight) ? string.Empty
                                                                         : $"{Digits(input.GlassHeight)}\" glass" }
                        .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" | ", parts);
    }
    private static bool IsPassageModel(string? value)
    {
        var compact = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return compact.Equals("FFPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("PASSFF", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("STPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("PASSST", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(compact, @"^STPASS(OD|IO)$", RegexOptions.IgnoreCase);
    }
    private static bool IsSeeThroughPassageModel(string? value)
    {
        var compact = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();

        return compact.Equals("STPASS", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("PASSST", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(compact, @"^STPASS(OD|IO)$", RegexOptions.IgnoreCase);
    }

    private static string PassageModelCode(string? value) => IsSeeThroughPassageModel(value) ? "STPASS" : "FFPASS";

    private static string PassageStyleCode(string? value) => IsSeeThroughPassageModel(value) ? "ST" : "FF";

    private static string[] PassagePriceAliases(string? model)
    {
        return IsSeeThroughPassageModel(model)
                   ? new[] { "STPASS", "PASSST", "PASSSEETHROUGH", "SEETHROUGHPASSAGE", "PASSAGESEETHROUGH" }
                   : new[] { "FFPASS",   "PASSFF", "PASSFRONTFACING", "FRONTFACINGPASSAGE", "PASSAGEFRONTFACING",
                             "PASSAGEFF" };
    }

    private static bool PassageCodeMatch(string compactText, string? model)
    {
        return PassagePriceAliases(model).Any(alias => compactText.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPassageRowText(string text, string compactText, string? model)
    {
        return text.Contains("passage", StringComparison.OrdinalIgnoreCase) || PassageCodeMatch(compactText, model);
    }

    private static bool IsPassageBaseExcludedOptionText(string text)
    {
        var value = text ?? string.Empty;

        return value.Contains("double glass", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("safety barrier", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("reflective", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("rgb", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("led", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("summit", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("burner", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("summer kit", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("power vent", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("media", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("stone", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("driftwood", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("birch", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("louver", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("screen", StringComparison.OrdinalIgnoreCase);
    }

    private static PriceRow? FindPassageBaseRow(IEnumerable<PriceRow> rows, string model)
    {
        var wantsSeeThrough = IsSeeThroughPassageModel(model);

        return Best(rows, r =>
                          {
                              if (!Eq(r.SheetName, "Indoor Price Book"))
                                  return false;

                              var text = Text(r);
                              var compact = Compact(text);

                              if (!IsPassageRowText(text, compact, model))
                                  return false;

                              if (IsPassageBaseExcludedOptionText(text))
                                  return false;

                              var saysSeeThrough = ContainsAll(text, new[] { "see", "through", "passage" }) ||
                                                   compact.Contains("STPASS", StringComparison.OrdinalIgnoreCase) ||
                                                   compact.Contains("PASSST", StringComparison.OrdinalIgnoreCase);

                              var saysFrontFacing = ContainsAll(text, new[] { "front", "facing", "passage" }) ||
                                                    compact.Contains("FFPASS", StringComparison.OrdinalIgnoreCase) ||
                                                    compact.Contains("PASSFF", StringComparison.OrdinalIgnoreCase);

                              return wantsSeeThrough
                                         ? saysSeeThrough || PassageCodeMatch(compact, model)
                                         : saysFrontFacing ||
                                               (!saysSeeThrough &&
                                                text.Contains("passage", StringComparison.OrdinalIgnoreCase)) ||
                                               PassageCodeMatch(compact, model);
                          });
    }

    private static bool PassageFeatureMatches(string text, string compact, string normalizedFeature)
    {
        var f = normalizedFeature ?? string.Empty;

        // Passage Outdoor Kit feature matching
        if (IsOutdoorKitFeature(f))
            return text.Contains("outdoor kit", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("passage", StringComparison.OrdinalIgnoreCase);
        if (f.Contains("double glass") || f.Contains("safety barrier"))
            return text.Contains("double glass", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("safety barrier", StringComparison.OrdinalIgnoreCase) ||
                   compact.Contains("DOUBLEGLASS", StringComparison.OrdinalIgnoreCase);

        if (f.Contains("reflective") && f.Contains("back"))
            return text.Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("back", StringComparison.OrdinalIgnoreCase);

        if (f.Contains("reflective") && f.Contains("side"))
            return text.Contains("reflective", StringComparison.OrdinalIgnoreCase) &&
                   (text.Contains("side", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("sides", StringComparison.OrdinalIgnoreCase));

        if (f.Contains("rgb") || f.Contains("led"))
            return compact.Contains("RGB", StringComparison.OrdinalIgnoreCase) ||
                   compact.Contains("LED", StringComparison.OrdinalIgnoreCase);

        if (f.Contains("summit"))
            return text.Contains("summit", StringComparison.OrdinalIgnoreCase);

        if (f.Contains("summer"))
            return text.Contains("summer", StringComparison.OrdinalIgnoreCase);

        if (f.Contains("power vent"))
            return text.Contains("power vent", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static PriceRow? FindPassageFeatureRow(IEnumerable<PriceRow> rows, string model, string feature)
    {
        var normalizedFeature = Normalize(feature);

        return Best(rows, r =>
                          {
                              if (!Eq(r.SheetName, "Indoor Price Book"))
                                  return false;

                              var text = Text(r);
                              var compact = Compact(text);

                              if (!IsPassageRowText(text, compact, model))
                                  return false;

                              return PassageFeatureMatches(text, compact, normalizedFeature);
                          });
    }
    private static string BuildDescription(FireplaceType type, string model, string size, string glassHeight)
    {
        // style-only OD/IO description
        if (IsIndoorOutdoorSeeThroughModelCode(model) && !IsPassageModel(model))
            return $"Indoor Outdoor See Through {Digits(size)}\" x {GlassInches(glassHeight)}\"";
        glassHeight = EffectiveGlassHeight(glassHeight, model);

        if (IsPassageModel(model))
            return type == FireplaceType.IndoorOutdoorSeeThrough && IsSeeThroughPassageModel(model)
                       ? "Indoor Outdoor See Through Passage 30\" x 60\""
                       : $"{ReadableStyle(type, model)} 30\" x 60\"";

        return type switch { FireplaceType.Traditional => $"Traditional {Digits(size)}\"",
                             FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough =>
                                 $"Outdoor {ReadableStyle(type, model)} {Digits(size)}\"",
                             FireplaceType.IndoorOutdoorSeeThrough =>
                                 $"Indoor Outdoor See Through {Digits(size)}\" x {GlassInches(glassHeight)}\"",
                             FireplaceType.Large => $"Large {ReadableStyle(type, model)} {Digits(size)}\"",
                             _ => $"{ReadableStyle(type, model)} {Digits(size)}\" x {GlassInches(glassHeight)}\"" };
    }
    private static string BuildModelNumber(FireplaceType type, string model, string size, string glassHeight)
    {
        // style-only OD/IO BuildModelNumber
        if (IsIndoorOutdoorSeeThroughModelCode(model) && !IsPassageModel(model))
        {
            var odBuildSuffix = GlassSuffix(glassHeight);
            var odBuildDigits = Digits(size);
            return $"ST-{odBuildDigits}{(string.IsNullOrWhiteSpace(odBuildSuffix) ? "" : "-" + odBuildSuffix)}-OD";
        }
        // block invalid Large See Through model number
        if (IsInvalidLargeSeeThroughModel(model, size))
            return string.Empty;
        if (IsPassageModel(model))
            return type == FireplaceType.IndoorOutdoorSeeThrough && IsSeeThroughPassageModel(model)
                       ? "STPASSOD"
                       : PassageModelCode(model);

        var suffix = GlassSuffix(glassHeight);
        var digits = Digits(size);

        if (type == FireplaceType.Traditional)
            return $"TR-{digits}";

        if (type is FireplaceType.Outdoor or FireplaceType.OutdoorSeeThrough)
        {
            var vfCode = VentFreeModelStyleCode(type, model);
            return $"{vfCode}-{digits}{(string.IsNullOrWhiteSpace(suffix) ? "" : "-" + suffix)}";
        }

        if (type == FireplaceType.IndoorOutdoorSeeThrough)
            return $"ST-{digits}{(string.IsNullOrWhiteSpace(suffix) ? "" : "-" + suffix)}-OD";

        var code = StyleCode(type, model);
        return $"{code}-{digits}{(string.IsNullOrWhiteSpace(suffix) ? "" : "-" + suffix)}";
    }

    private static string OutdoorVentFreeScreenDescription(FireplaceType type, string model)
    {
        var style = VentFreeModelStyleCode(type, model);

        return style.Equals("VFF", StringComparison.OrdinalIgnoreCase) ? "Invisible Mesh Safety Screen"
                                                                       : "Invisible Mesh Safety Screens";
    }
    private static string StyleCode(FireplaceType type, string model)
    {
        if (IsPassageModel(model))
            return PassageStyleCode(model);

        var value = Normalize(model);

        // manual TR style-code override
        var compactStyleModel = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();
        if (compactStyleModel.Equals("TR", StringComparison.OrdinalIgnoreCase) ||
            compactStyleModel.Equals("TRA", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(compactStyleModel, @"^(TR|TRA)\d{2,3}$"))
            return "TR";
        if (type == FireplaceType.IndoorOutdoorSeeThrough)
            return "ST";
        if (type == FireplaceType.OutdoorSeeThrough)
            return "ST";
        if (type == FireplaceType.Outdoor)
            return value.Contains("see") ? "ST" : "FF";
        if (type == FireplaceType.Traditional)
            return "TR";
        if (value.Contains("room") || value.Contains("rd"))
            return "RD";
        if (value.Contains("double corner") || value.Contains("dc"))
            return "DC";
        if (value.Contains("left") || value.Contains("lc"))
            return "LC";
        if (value.Contains("right") || value.Contains("rc"))
            return "RC";
        if (value.Contains("see") || value.Contains("st"))
            return "ST";
        return "FF";
    }

    private static string OutdoorResourceStyleCode(FireplaceType type, string model)
    {
        var value = Normalize(model);

        // manual TR style-code override
        var compactStyleModel = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();
        if (compactStyleModel.Equals("TR", StringComparison.OrdinalIgnoreCase) ||
            compactStyleModel.Equals("TRA", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(compactStyleModel, @"^(TR|TRA)\d{2,3}$"))
            return "TR";
        var indoorStyle = StyleCode(type, model ?? string.Empty);

        if (type == FireplaceType.OutdoorSeeThrough || indoorStyle == "ST" || value.Contains("see") ||
            value.Contains("st"))
            return "VST";
        if (indoorStyle == "LC" || value.Contains("left") || value.Contains("vlc"))
            return "VLC";
        if (indoorStyle == "RC" || value.Contains("right") || value.Contains("vrc"))
            return "VRC";
        if (indoorStyle == "DC" || value.Contains("double") || value.Contains("vdc"))
            return "VDC";
        return "VFF";
    }
    private static string[] StyleWords(FireplaceType type, string model)
    {
        if (IsPassageModel(model))
            return IsSeeThroughPassageModel(model) ? ["see", "through", "passage"] : ["front", "facing", "passage"];

        var code = StyleCode(type, model);
        return code switch { "TR" => ["traditional"],     "ST" => ["see", "through"],   "LC" => ["left", "corner"],
                             "RC" => ["right", "corner"], "DC" => ["double", "corner"], "RD" => ["room", "definer"],
                             _ => ["front", "facing"] };
    }

    private static string[] ResourceStyle(FireplaceType type, string model)
    {
        if (type == FireplaceType.Traditional)
            return ["traditional"];
        if (type == FireplaceType.IndoorOutdoorSeeThrough)
            return ["see", "through"];
        return StyleWords(type, model);
    }
    private static string ReadableStyle(FireplaceType type, string model)
    {
        if (IsPassageModel(model))
            return IsSeeThroughPassageModel(model) ? "See Through Passage" : "Front Facing Passage";

        var code = StyleCode(type, model);
        return code switch { "TR" => "Traditional",  "ST" => "See Through",   "LC" => "Left Corner",
                             "RC" => "Right Corner", "DC" => "Double Corner", "RD" => "Room Definer",
                             _ => "Front Facing" };
    }

    private static string Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value, @"\D", string.Empty);
    }
    private static string GlassInches(string glassHeight)
    {
        var text = (glassHeight ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = Regex.Replace(text.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

        // Order matters: EH contains H, so Extra High must be checked first.
        if (compact == "EH" || compact.Contains("EXTRAHIGH") || compact.Contains("30"))
            return "30";
        if (compact == "H" || compact.Contains("HIGH") || compact.Contains("24"))
            return "24";
        if (compact == "R" || compact.Contains("REGULAR") || compact.Contains("STANDARD") || compact.Contains("16"))
            return "16";

        return Digits(glassHeight);
    }

    private static string GlassSuffix(string glassHeight) => GlassInches(glassHeight) switch { "16" => "R", "24" => "H",
                                                                                               "30" => "EH",
                                                                                               _ => string.Empty };
    private static bool ContainsGlass(PriceRow row, string glass)
    {
        var text = Text(row);
        return text.Contains($"x{glass}", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"x {glass}", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{glass}\"", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{glass}”", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{glass}″", StringComparison.OrdinalIgnoreCase) ||
               (glass == "16" && text.Contains("regular", StringComparison.OrdinalIgnoreCase)) ||
               (glass == "24" && text.Contains("high", StringComparison.OrdinalIgnoreCase)) ||
               (glass == "30" && text.Contains("extra high", StringComparison.OrdinalIgnoreCase));
    }
    private static bool ContainsSize(PriceRow row, string size) =>
        string.IsNullOrWhiteSpace(size) || Regex.IsMatch(Text(row), $@"(?<!\d){Regex.Escape(size)}(?!\d)");
    private static bool ContainsAll(string text, IEnumerable<string> parts) =>
        parts.All(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
    private static PriceRow? Best(IEnumerable<PriceRow> rows,
                                  Func<PriceRow, bool> predicate) => rows.FirstOrDefault(predicate);
    private static bool Eq(string a, string b) => string.Equals(a?.Trim(), b?.Trim(),
                                                                StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
    private static string EffectiveGlassHeight(string? glassHeight, string? model)
    {
        if (IsPassageModel(model))
            return "60";

        return FirstNonBlank(NormalizeGlassHeightAlias(glassHeight), ExtractGlassHeightFromModelCode(model));
    }

    private static string NormalizeGlassHeightAlias(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var number = Regex.Match(text, @"\d+").Value;
        if (!string.IsNullOrWhiteSpace(number))
            return number;

        // Check EH before H so 30\" glass is never accidentally parsed as 24\".
        if (Regex.IsMatch(text, @"(?i)^\s*EH\s*$"))
            return "30";
        if (Regex.IsMatch(text, @"(?i)^\s*H\s*$"))
            return "24";
        if (Regex.IsMatch(text, @"(?i)^\s*R\s*$"))
            return "16";

        return string.Empty;
    }
    private static string ExtractGlassHeightFromModelCode(string? value)
    {
        if (IsPassageModel(value))
            return "60";

        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Examples: FF-80-H, FF80H, FF-80-EH, FF80EH, DVDR45R.
        // EH is listed before H so the 30" suffix wins correctly.
        var match = Regex.Match(text, @"(?i)\b[A-Z]{1,10}[-\s]*\d{2,3}[-\s]*(EH|H|R)(?:[-\s]*(OD|IO))?\b");
        return match.Success ? NormalizeGlassHeightAlias(match.Groups[1].Value) : string.Empty;
    }

    private static string Text(PriceRow r) =>
        $"{r.PartName} {r.Description} {r.Sku} {string.Join(" ", r.RawValues.Values)}";
    private static string First(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return string.Empty;
    }

    private static decimal? ParsePrice(string value)
    {
        value = new string((value ?? string.Empty).Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
    private static bool IsInvalidLargeSeeThroughModel(string? model, string? size = null)
    {
        var raw = model ?? string.Empty;
        var compact = Regex.Replace(raw, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();
        var normalized = Normalize(raw);

        var digits = Digits(size);

        if (string.IsNullOrWhiteSpace(digits))
        {
            var match = Regex.Match(compact, @"(?:LDVST|ST)(\d{3})");
            if (match.Success)
                digits = match.Groups[1].Value;
        }

        if (!int.TryParse(digits, out var numericSize) || numericSize < 120)
            return false;

        return compact.StartsWith("LDVST", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(compact, @"^ST\d{3}(R|H|EH)?$", RegexOptions.IgnoreCase) ||
               normalized.Contains("large see through", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("large see-through", StringComparison.OrdinalIgnoreCase);
    }

    private static FireplaceType DetectType(string model, string size = "")
    {
        // OD/IO model code type detection
        if (IsIndoorOutdoorSeeThroughModelCode(model))
            return FireplaceType.IndoorOutdoorSeeThrough;
        // block invalid Large See Through type detection
        if (IsInvalidLargeSeeThroughModel(model, size))
            return FireplaceType.Unknown;
        if (IsPassageModel(model))
            return IsSeeThroughPassageModel(model) ? FireplaceType.IndoorSeeThrough : FireplaceType.Indoor;

        var value = (model ?? string.Empty).ToLowerInvariant();
        var normalized = Regex.Replace(value, @"[^a-z0-9]+", " ").Trim();
        var compactModel = Regex.Replace(model ?? string.Empty, @"[^A-Za-z0-9]+", string.Empty).ToUpperInvariant();
        var sizeDigits = Digits(size);

        // Outdoor Vent Free compact model detection
        if (IsOutdoorVentFreeResourceModel(compactModel))
        {
            return compactModel.StartsWith("VST", StringComparison.OrdinalIgnoreCase) ||
                           compactModel.StartsWith("VFST", StringComparison.OrdinalIgnoreCase)
                       ? FireplaceType.OutdoorSeeThrough
                       : FireplaceType.Outdoor;
        }
        if (compactModel.Equals("TR", StringComparison.OrdinalIgnoreCase) ||
            compactModel.Equals("TRA", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(compactModel, @"^(TR|TRA)\d{2,3}$") ||
            compactModel.StartsWith("TRAD", StringComparison.OrdinalIgnoreCase) ||
            compactModel.Contains("TRADITIONAL", StringComparison.OrdinalIgnoreCase))
            return FireplaceType.Traditional;
        if (int.TryParse(sizeDigits, out var sizeNumber) && sizeNumber >= 120)
            return FireplaceType.Large;

        if (normalized.Contains("traditional") || normalized.Contains("dvtra") || normalized.Contains("trabon") ||
            normalized.Contains("tra bon") || Regex.IsMatch(normalized, @"\btr\b|\btra\b|\btrad\b"))
            return FireplaceType.Traditional;
        if (normalized.Contains("large") || normalized.Contains("long"))
            return FireplaceType.Large;

        var isSeeThrough =
            normalized.Contains("see through") || Regex.IsMatch(normalized, @"\bst\b") || normalized.Contains("st od");
        var isIndoorOutdoor = normalized.Contains("indoor outdoor") || normalized.Contains("indooroutdoor") ||
                              normalized.Contains("st od");
        var isVentFreeOutdoor =
            normalized.Contains("vent free") || Regex.IsMatch(normalized, @"\bvf\b|\bvff\b|\bvst\b");
        var isOutdoor = normalized.Contains("outdoor") || isVentFreeOutdoor;

        if (isSeeThrough && isIndoorOutdoor)
            return FireplaceType.IndoorOutdoorSeeThrough;
        if (isOutdoor)
            return isSeeThrough || normalized.Contains("vst") ? FireplaceType.OutdoorSeeThrough : FireplaceType.Outdoor;
        if (isSeeThrough)
            return FireplaceType.IndoorSeeThrough;
        return FireplaceType.Indoor;
    }

    private static string GetDefaultPricingPath()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "LocalData", "pricing.xlsx"),
                                            Path.Combine(Environment.CurrentDirectory, "LocalData", "pricing.xlsx"),
                                            Path.Combine(AppContext.BaseDirectory, "pricing.xlsx"),
                                            Path.Combine(Environment.CurrentDirectory, "pricing.xlsx") };

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var dir = baseDir; dir is not null; dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "LocalData", "pricing.xlsx"));

            if (File.Exists(Path.Combine(dir.FullName, "FlareQuotes.App", "FlareQuotes.App.csproj")))
                break;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
