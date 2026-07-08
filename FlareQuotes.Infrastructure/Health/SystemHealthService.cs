using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Infrastructure.Health;

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly ISettingsService _settingsService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly IAppLogger _logger;

    public SystemHealthService(
        ISettingsService settingsService,
        ISecurityAuditService securityAuditService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _securityAuditService = securityAuditService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemHealthItem>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SystemHealthItem>();

        items.Add(CheckFile("Pricing workbook", Path.Combine(AppContext.BaseDirectory, "LocalData", "pricing.xlsx")));
        items.Add(CheckFile("Resource links workbook", Path.Combine(AppContext.BaseDirectory, "LocalData", "resource_links.xlsx")));
        items.Add(CheckFile("Outdoor links workbook", Path.Combine(AppContext.BaseDirectory, "LocalData", "outdoor_spec_center_extracted_links.xlsx")));

        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
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

    private static SystemHealthItem CheckFile(string name, string path)
    {
        return new SystemHealthItem
        {
            Name = name,
            Detail = File.Exists(path) ? $"Found: {Path.GetFileName(path)}" : $"Missing: {path}",
            State = File.Exists(path) ? SystemHealthState.Ok : SystemHealthState.Error
        };
    }

    private static SystemHealthItem CheckUpdateFeed(string updateManifestUrl)
    {
        return new SystemHealthItem
        {
            Name = "Update feed",
            Detail = Uri.TryCreate(updateManifestUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? "HTTPS update manifest configured."
                : "Update manifest URL is missing or not HTTPS.",
            State = Uri.TryCreate(updateManifestUrl, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps
                ? SystemHealthState.Ok
                : SystemHealthState.Error
        };
    }

    private static SystemHealthItem CheckRuntimeConfig()
    {
        var runtimeConfig = Directory.GetFiles(AppContext.BaseDirectory, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly)
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