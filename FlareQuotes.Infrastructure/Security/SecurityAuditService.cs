using System.Text.Json;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.Infrastructure.Security;

public sealed class SecurityAuditService : ISecurityAuditService
{
    public Task<IReadOnlyList<SystemHealthItem>> AuditAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<SystemHealthItem>();

        var currentTokenDirectory = Path.Combine(AppPaths.Root, "GmailToken");
        var tokenDirectories = new[] { currentTokenDirectory }
            .Concat(AppPaths.LegacyRoots.Select(root => Path.Combine(root, "gmail-token")))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokenDirectories.Length == 0)
        {
            items.Add(new SystemHealthItem {
                Name = "Gmail token store",
                Detail = "No Gmail token store found yet. It will be created after Gmail is connected.",
                State = SystemHealthState.Warning
            });
        }
        else
        {
            var tokenFiles = tokenDirectories
                .SelectMany(directory => Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
                .ToArray();
            var protectedTokens = tokenFiles.Where(path =>
                path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)).ToArray();
            var plaintextTokens = tokenFiles.Where(path =>
                !path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) && LooksLikePlaintextToken(path)).ToArray();

            items.Add(new SystemHealthItem {
                Name = "Gmail token store",
                Detail =
                    plaintextTokens.Length == 0
                        ? $"DPAPI token protection active. Protected token files found: {protectedTokens.Length}."
                        : $"Plain-text token files found: {plaintextTokens.Length}. Reconnect Gmail to migrate/remove them.",
                State = plaintextTokens.Length == 0 ? SystemHealthState.Ok : SystemHealthState.Error
            });
        }

        var settingsPath = AppPaths.SettingsFile;

        items.Add(AuditSettingsFile(settingsPath));

        return Task.FromResult<IReadOnlyList<SystemHealthItem>>(items);
    }

    private static bool LooksLikePlaintextToken(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 2 * 1024 * 1024)
                return false;

            var text = File.ReadAllText(path);
            return text.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("AccessToken", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static SystemHealthItem AuditSettingsFile(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new SystemHealthItem { Name = "Settings storage", Detail = "Settings file has not been created yet.",
                                          State = SystemHealthState.Warning };
        }

        try
        {
            if (new FileInfo(settingsPath).Length > 1024 * 1024)
            {
                return new SystemHealthItem {
                    Name = "Settings storage",
                    Detail = "Settings file exceeds the maximum allowed size and will be ignored.",
                    State = SystemHealthState.Error
                };
            }

            var text = File.ReadAllText(settingsPath);
            using var _ = JsonDocument.Parse(text);

            var sensitiveMarkers =
                new[] { "access_token", "refresh_token", "client_secret", "private" + "_" + "key", "Bearer " };
            var hasSensitiveValue =
                sensitiveMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

            return new SystemHealthItem {
                Name = "Settings storage",
                Detail = hasSensitiveValue
                             ? "Settings file contains sensitive token-like data. Move secrets to secure storage."
                             : "Settings file is readable and does not contain obvious token secrets.",
                State = hasSensitiveValue ? SystemHealthState.Error : SystemHealthState.Ok
            };
        }
        catch
        {
            return new SystemHealthItem {
                Name = "Settings storage",
                Detail = "Settings file exists but could not be parsed. The app will rebuild defaults if needed.",
                State = SystemHealthState.Warning
            };
        }
    }
}
