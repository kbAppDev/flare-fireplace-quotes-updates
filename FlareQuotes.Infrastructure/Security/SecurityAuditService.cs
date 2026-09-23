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

        items.Add(AuditTokenStores(AppPaths.GmailTokenAuditStores));

        var settingsPath = AppPaths.SettingsFile;

        items.Add(AuditSettingsFile(settingsPath));
        items.Add(AuditLegacyGmailCredentialCopy());

        return Task.FromResult<IReadOnlyList<SystemHealthItem>>(items);
    }

    internal static SystemHealthItem AuditTokenStores(IEnumerable<string> tokenStorePaths)
    {
        var roots = tokenStorePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeDirectories = roots.Where(Directory.Exists).ToArray();
        var archiveDirectories = roots.SelectMany(FindReconnectArchives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tokenDirectories = activeDirectories.Concat(archiveDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokenDirectories.Length == 0)
        {
            return new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = "No Gmail token store found yet. It will be created after Gmail is connected.",
                State = SystemHealthState.Warning
            };
        }

        var tokenFiles = tokenDirectories.SelectMany(GetTokenFiles).ToArray();
        var protectedTokens = tokenFiles.Where(path =>
            path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)).ToArray();
        var plaintextTokens = tokenFiles.Where(path =>
            !path.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) && LooksLikePlaintextToken(path)).ToArray();

        if (plaintextTokens.Length > 0)
        {
            return new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = $"Plain-text token files found: {plaintextTokens.Length}. Reconnect Gmail to replace them with protected credentials.",
                State = SystemHealthState.Error
            };
        }

        if (archiveDirectories.Length > 0)
        {
            return new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = $"Protected token files: {protectedTokens.Length}. Reconnect archives awaiting cleanup: {archiveDirectories.Length}.",
                State = SystemHealthState.Warning
            };
        }

        if (protectedTokens.Length == 0)
        {
            return new SystemHealthItem
            {
                Name = "Gmail token store",
                Detail = "The Gmail token folder exists but contains no protected authorization token. Reconnect Gmail.",
                State = SystemHealthState.Warning
            };
        }

        return new SystemHealthItem
        {
            Name = "Gmail token store",
            Detail = $"DPAPI token protection active. Protected token files found: {protectedTokens.Length}.",
            State = SystemHealthState.Ok
        };
    }

    private static IEnumerable<string> FindReconnectArchives(string tokenStorePath)
    {
        var parent = Directory.GetParent(tokenStorePath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return [];

        var folderName = Path.GetFileName(
            tokenStorePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            return Directory.GetDirectories(parent, $"{folderName}.reconnect-*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> GetTokenFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
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
            return new SystemHealthItem
            {
                Name = "Settings storage",
                Detail = "Settings file has not been created yet.",
                State = SystemHealthState.Warning
            };
        }

        try
        {
            if (new FileInfo(settingsPath).Length > 1024 * 1024)
            {
                return new SystemHealthItem
                {
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

    private static SystemHealthItem AuditLegacyGmailCredentialCopy()
    {
        var legacyPath = Path.Combine(AppPaths.Root, "gmail_credentials.json");
        try
        {
            var info = new FileInfo(legacyPath);
            if (!info.Exists)
            {
                return new SystemHealthItem
                {
                    Name = "Gmail credential storage",
                    Detail = "No obsolete root-level Gmail credential copy was found.",
                    State = SystemHealthState.Ok
                };
            }

            return new SystemHealthItem
            {
                Name = "Gmail credential storage",
                Detail = "An obsolete Gmail credential copy remains outside the protected Credentials folder.",
                State = SystemHealthState.Warning
            };
        }
        catch
        {
            return new SystemHealthItem
            {
                Name = "Gmail credential storage",
                Detail = "The obsolete Gmail credential location could not be checked.",
                State = SystemHealthState.Warning
            };
        }
    }
}
