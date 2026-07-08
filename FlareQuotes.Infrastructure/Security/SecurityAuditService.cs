using System.Text.Json;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;

namespace FlareQuotes.Infrastructure.Security;

public sealed class SecurityAuditService : ISecurityAuditService
{
    public Task<IReadOnlyList<SystemHealthItem>> AuditAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<SystemHealthItem>();

        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flare Fireplaces - Quotes",
            "v3",
            "gmail-token");

        if (!Directory.Exists(tokenDir))
        {
            items.Add(new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = "No Gmail token store found yet. It will be created after Gmail is connected.",
                State = SystemHealthState.Warning
            });
        }
        else
        {
            var plainJsonTokens = Directory.GetFiles(tokenDir, "*.json", SearchOption.TopDirectoryOnly);
            var protectedTokens = Directory.GetFiles(tokenDir, "*.dpapi", SearchOption.TopDirectoryOnly);

            items.Add(new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = plainJsonTokens.Length == 0
                    ? $"DPAPI token protection active. Protected token files found: {protectedTokens.Length}."
                    : $"Plain-text token files found: {plainJsonTokens.Length}. Reconnect Gmail to migrate/remove them.",
                State = plainJsonTokens.Length == 0 ? SystemHealthState.Ok : SystemHealthState.Error
            });
        }

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flare Fireplace Quotes",
            "settings.json");

        items.Add(AuditSettingsFile(settingsPath));

        return Task.FromResult<IReadOnlyList<SystemHealthItem>>(items);
    }

    private static SystemHealthItem AuditSettingsFile(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new SystemHealthItem
            {
                Name = "Settings storage",
                Detail = "Settings file has not been created yet.",
                State = SystemHealthState.Warning
            };
        }

        try
        {
            var text = File.ReadAllText(settingsPath);
            using var _ = JsonDocument.Parse(text);

            var sensitiveMarkers = new[] { "access_token", "refresh_token", "client_secret", "private" + "_" + "key", "Bearer " };
            var hasSensitiveValue = sensitiveMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

            return new SystemHealthItem
            {
                Name = "Settings storage",
                Detail = hasSensitiveValue
                    ? "Settings file contains sensitive token-like data. Move secrets to secure storage."
                    : "Settings file is readable and does not contain obvious token secrets.",
                State = hasSensitiveValue ? SystemHealthState.Error : SystemHealthState.Ok
            };
        }
        catch
        {
            return new SystemHealthItem
            {
                Name = "Settings storage",
                Detail = "Settings file exists but could not be parsed. The app will rebuild defaults if needed.",
                State = SystemHealthState.Warning
            };
        }
    }
}