using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Security;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class SecurityAuditServiceTests
{
    [Fact]
    public void AuditTokenStoresReportsHistoricalGoogleTokenFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareSecurityAuditTests", Guid.NewGuid().ToString("N"));
        var gmailLegacy = Path.Combine(root, "GoogleToken");
        var certBuilderLegacy = Path.Combine(root, "GoogleTokenCertBuilder");
        Directory.CreateDirectory(gmailLegacy);
        Directory.CreateDirectory(certBuilderLegacy);
        File.WriteAllText(Path.Combine(gmailLegacy, "TokenResponse-user"), "{\"refresh_token\":\"fixture\"}");
        File.WriteAllText(Path.Combine(certBuilderLegacy, "TokenResponse-cert-builder-user"),
                          "{\"access_token\":\"fixture\"}");

        try
        {
            var result = SecurityAuditService.AuditTokenStores([gmailLegacy, certBuilderLegacy]);

            Assert.Equal(SystemHealthState.Error, result.State);
            Assert.Contains("2", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AuditTokenStoresReportsReconnectArchives()
    {
        var root = CreateTestRoot();
        var tokenDirectory = Path.Combine(root, "GmailToken");
        var archiveDirectory = Path.Combine(root, "GmailToken.reconnect-20260923-120000");
        Directory.CreateDirectory(tokenDirectory);
        Directory.CreateDirectory(archiveDirectory);
        File.WriteAllText(Path.Combine(tokenDirectory, "active.dpapi"), "protected");
        File.WriteAllText(Path.Combine(archiveDirectory, "previous.dpapi"), "protected");

        try
        {
            var result = SecurityAuditService.AuditTokenStores([tokenDirectory]);

            Assert.Equal(SystemHealthState.Warning, result.State);
            Assert.Contains("Reconnect archives awaiting cleanup: 1", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AuditTokenStoresScansReconnectArchivesForPlaintextTokens()
    {
        var root = CreateTestRoot();
        var tokenDirectory = Path.Combine(root, "GmailToken");
        var archiveDirectory = Path.Combine(root, "GmailToken.reconnect-20260923-120000");
        Directory.CreateDirectory(archiveDirectory);
        File.WriteAllText(Path.Combine(archiveDirectory, "legacy-token.json"),
                          "{\"access_token\":\"plaintext\"}");

        try
        {
            var result = SecurityAuditService.AuditTokenStores([tokenDirectory]);

            Assert.Equal(SystemHealthState.Error, result.State);
            Assert.Contains("Plain-text token files found: 1", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareSecurityAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
