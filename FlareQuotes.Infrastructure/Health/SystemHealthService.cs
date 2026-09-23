using ClosedXML.Excel;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Updates;
using FlareQuotes.Infrastructure.Excel;

namespace FlareQuotes.Infrastructure.Health;

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly ISettingsService _settingsService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly IAppLogger _logger;

    public SystemHealthService(ISettingsService settingsService, ISecurityAuditService securityAuditService,
                               IAppLogger logger)
    {
        _settingsService = settingsService;
        _securityAuditService = securityAuditService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemHealthItem>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SystemHealthItem>();
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var localDataPath = Path.Combine(AppContext.BaseDirectory, "LocalData");
        var bundledPricingPath = Path.Combine(localDataPath, "pricing.xlsx");
        var pricingPath = string.IsNullOrWhiteSpace(settings.PricingFile) ? bundledPricingPath : settings.PricingFile;

        items.Add(CheckPricingWorkbook(pricingPath, cancellationToken));
        items.Add(CheckResourceLinksWorkbook(Path.Combine(localDataPath, "resource_links.xlsx"), cancellationToken));
        items.Add(CheckOutdoorLinksWorkbook(
            Path.Combine(localDataPath, "outdoor_spec_center_extracted_links.xlsx"), cancellationToken));

        items.Add(CheckUpdateFeed(settings.UpdateManifestUrl));
        items.Add(CheckRuntimeConfig());

        var securityItems = await _securityAuditService.AuditAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(securityItems);

        items.Add(new SystemHealthItem
        {
            Name = "Redacted app logging",
            Detail = $"Active. Log file: {_logger.LogFilePath}",
            State = SystemHealthState.Ok
        });

        return items;
    }

    internal static SystemHealthItem CheckPricingWorkbook(string path,
                                                          CancellationToken cancellationToken = default)
    {
        return CheckWorkbook("Pricing workbook", path, workbook =>
        {
            var pricingSheet = workbook.Worksheets.FirstOrDefault(sheet =>
                HasRequiredHeaders(sheet.Row(1), ["SKU", "MSRP"]) ||
                HasRequiredHeaders(sheet.Row(1), ["SKU", "Price"]));
            if (pricingSheet is null)
                return (false, "No worksheet contains the required SKU and price columns.");

            var dataRows = pricingSheet.RowsUsed().Count(row => row.RowNumber() > 1);
            return dataRows > 0
                       ? (true, $"Valid pricing schema. Sheets: {workbook.Worksheets.Count}; data rows: {dataRows}.")
                       : (false, "The pricing worksheet has headers but no price rows.");
        }, cancellationToken);
    }

    internal static SystemHealthItem CheckResourceLinksWorkbook(string path,
                                                                CancellationToken cancellationToken = default)
    {
        return CheckWorkbook("Resource links workbook", path, workbook =>
        {
            if (!workbook.TryGetWorksheet("Resource Links", out var sheet))
                return (false, "Required worksheet 'Resource Links' is missing.");

            if (!HasRequiredHeaders(sheet.Row(1), ["Template", "Model #", "Product Sheet", "Wood Framing"]))
                return (false, "Required resource-link columns are missing.");

            var dataRows = sheet.RowsUsed().Count(row => row.RowNumber() > 1);
            return dataRows > 0
                       ? (true, $"Valid resource-link schema. Model rows: {dataRows}.")
                       : (false, "The resource-link worksheet has headers but no model rows.");
        }, cancellationToken);
    }

    internal static SystemHealthItem CheckOutdoorLinksWorkbook(string path,
                                                               CancellationToken cancellationToken = default)
    {
        return CheckWorkbook("Outdoor links workbook", path, workbook =>
        {
            if (!workbook.TryGetWorksheet("App Resource Rows", out var sheet))
                return (false, "Required worksheet 'App Resource Rows' is missing.");
            if (workbook.Worksheets.Count != 1)
                return (false, "The outdoor resource workbook contains unexpected worksheets.");

            string[] requiredHeaders =
                ["Model Key", "Compact Key", "Product Sheet", "Framing Guide", "Dimension File", "CAD", "SketchUp", "Revit"];
            var actualHeaders = sheet.Row(1).CellsUsed().Select(cell => cell.GetString().Trim()).ToArray();
            if (!actualHeaders.SequenceEqual(requiredHeaders, StringComparer.OrdinalIgnoreCase))
                return (false, "Required outdoor resource-link columns are missing.");

            var dataRows = sheet.RowsUsed().Count(row => row.RowNumber() > 1);
            return dataRows > 0
                       ? (true, $"Valid outdoor resource-link schema. Model rows: {dataRows}.")
                       : (false, "The outdoor resource-link worksheet has headers but no model rows.");
        }, cancellationToken);
    }

    private static SystemHealthItem CheckWorkbook(string name, string path,
                                                  Func<XLWorkbook, (bool IsValid, string Detail)> validate,
                                                  CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new SystemHealthItem
            {
                Name = name,
                Detail = $"Missing: {Path.GetFileName(path)}",
                State = SystemHealthState.Error
            };
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workbookSnapshot =
                ClosedXmlPriceBookService.CreateValidatedWorkbookSnapshot(path, cancellationToken);
            if (workbookSnapshot is null)
                return InvalidWorkbook(
                    name, "The workbook is not a valid XLSX file or failed safety and structure validation.");

            cancellationToken.ThrowIfCancellationRequested();
            workbookSnapshot.Position = 0;
            using var workbook = new XLWorkbook(workbookSnapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (workbook.Worksheets.Count == 0)
                return InvalidWorkbook(name, "The workbook contains no worksheets.");

            var result = validate(workbook);
            return new SystemHealthItem
            {
                Name = name,
                Detail = result.Detail,
                State = result.IsValid ? SystemHealthState.Ok : SystemHealthState.Error
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FileFormatException or
                                   UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InvalidWorkbook(name, "The workbook could not be opened as a valid XLSX file.");
        }
    }

    private static bool HasRequiredHeaders(IXLRow row, IReadOnlyCollection<string> requiredHeaders)
    {
        var headers = row.CellsUsed().Select(cell => cell.GetString().Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requiredHeaders.All(headers.Contains);
    }

    private static SystemHealthItem InvalidWorkbook(string name, string detail)
    {
        return new SystemHealthItem { Name = name, Detail = detail, State = SystemHealthState.Error };
    }

    private static SystemHealthItem CheckUpdateFeed(string updateManifestUrl)
    {
        var trusted = UpdateTrustPolicy.TryGetTrustedManifestUri(updateManifestUrl, out _);
        return new SystemHealthItem
        {
            Name = "Update feed",
            Detail = trusted
                         ? "Pinned Flare GitHub update manifest configured."
                         : "Update manifest URL is outside the pinned release lane.",
            State = trusted ? SystemHealthState.Ok : SystemHealthState.Error
        };
    }

    private static SystemHealthItem CheckRuntimeConfig()
    {
        var runtimeConfig =
            Directory.GetFiles(AppContext.BaseDirectory, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(runtimeConfig))
        {
            return new SystemHealthItem
            {
                Name = "Runtime packaging",
                Detail = "runtimeconfig.json not found. This is normal only for some single-file builds.",
                State = SystemHealthState.Warning
            };
        }

        var text = File.ReadAllText(runtimeConfig);
        var hasSharedFrameworkDependency = text.Contains("\"frameworks\"", StringComparison.OrdinalIgnoreCase);

        return new SystemHealthItem
        {
            Name = "Runtime packaging",
            Detail = hasSharedFrameworkDependency
                         ? "Framework-dependent runtime config detected. Release builds should be self-contained."
                         : "Self-contained runtime config verified.",
            State = hasSharedFrameworkDependency ? SystemHealthState.Warning : SystemHealthState.Ok
        };
    }
}
