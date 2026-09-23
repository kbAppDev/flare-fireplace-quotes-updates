using FlareQuotes.Infrastructure.Gmail;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class GmailReconnectRegressionTests
{
    [Fact]
    public void ArchiveExistingTokenStoreMovesTokenFilesWithoutTouchingCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareGmailReconnectTests", Guid.NewGuid().ToString("N"));
        var tokenDirectory = Path.Combine(root, "GmailToken");
        var credentialDirectory = Path.Combine(root, "Credentials");
        var tokenFile = Path.Combine(tokenDirectory, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user");
        var credentialFile = Path.Combine(credentialDirectory, "gmail_credentials.json");

        Directory.CreateDirectory(tokenDirectory);
        Directory.CreateDirectory(credentialDirectory);
        File.WriteAllText(tokenFile, "protected-token");
        File.WriteAllText(credentialFile, "client-credentials");

        try
        {
            var archivedDirectory = GmailDraftService.ArchiveExistingTokenStore(tokenDirectory);

            Assert.NotNull(archivedDirectory);
            Assert.False(Directory.Exists(tokenDirectory));
            Assert.True(Directory.Exists(archivedDirectory));
            Assert.Equal(
                "protected-token",
                File.ReadAllText(Path.Combine(archivedDirectory!, Path.GetFileName(tokenFile))));
            Assert.Equal("client-credentials", File.ReadAllText(credentialFile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveExistingTokenStoreAllowsAuthorizationWhenNoSavedTokenExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareGmailReconnectTests", Guid.NewGuid().ToString("N"));
        var tokenDirectory = Path.Combine(root, "GmailToken");

        try
        {
            Assert.Null(GmailDraftService.ArchiveExistingTokenStore(tokenDirectory));

            Directory.CreateDirectory(tokenDirectory);
            Assert.Null(GmailDraftService.ArchiveExistingTokenStore(tokenDirectory));
            Assert.False(Directory.Exists(tokenDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreArchivedTokenStoreReplacesPartialAuthorizationWithPreviousToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareGmailReconnectTests", Guid.NewGuid().ToString("N"));
        var tokenDirectory = Path.Combine(root, "GmailToken");
        Directory.CreateDirectory(tokenDirectory);
        File.WriteAllText(Path.Combine(tokenDirectory, "previous.dpapi"), "previous-token");

        try
        {
            var archivedDirectory = GmailDraftService.ArchiveExistingTokenStore(tokenDirectory);
            Assert.NotNull(archivedDirectory);

            Directory.CreateDirectory(tokenDirectory);
            File.WriteAllText(Path.Combine(tokenDirectory, "partial.dpapi"), "partial-token");

            GmailDraftService.RestoreArchivedTokenStore(tokenDirectory, archivedDirectory);

            Assert.True(File.Exists(Path.Combine(tokenDirectory, "previous.dpapi")));
            Assert.Equal("previous-token", File.ReadAllText(Path.Combine(tokenDirectory, "previous.dpapi")));
            Assert.False(File.Exists(Path.Combine(tokenDirectory, "partial.dpapi")));
            Assert.False(Directory.Exists(archivedDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupReconnectArchivesKeepsOnlyRequestedNewestArchives()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareGmailReconnectTests", Guid.NewGuid().ToString("N"));
        var tokenDirectory = Path.Combine(root, "GmailToken");
        Directory.CreateDirectory(root);

        var oldest = Path.Combine(root, "GmailToken.reconnect-20260101-010101");
        var middle = Path.Combine(root, "GmailToken.reconnect-20260102-010101");
        var newest = Path.Combine(root, "GmailToken.reconnect-20260103-010101");
        Directory.CreateDirectory(oldest);
        Directory.CreateDirectory(middle);
        Directory.CreateDirectory(newest);
        Directory.SetLastWriteTimeUtc(oldest, new DateTime(2026, 1, 1, 1, 1, 1, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(middle, new DateTime(2026, 1, 2, 1, 1, 1, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(newest, new DateTime(2026, 1, 3, 1, 1, 1, DateTimeKind.Utc));

        try
        {
            var removed = GmailDraftService.CleanupReconnectArchives(tokenDirectory, maxArchivesToKeep: 1);

            Assert.Equal(2, removed);
            Assert.True(Directory.Exists(newest));
            Assert.False(Directory.Exists(middle));
            Assert.False(Directory.Exists(oldest));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreWithoutPreviousTokenRemovesPartialAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlareGmailReconnectTests", Guid.NewGuid().ToString("N"));
        var tokenDirectory = Path.Combine(root, "GmailToken");
        Directory.CreateDirectory(tokenDirectory);
        File.WriteAllText(Path.Combine(tokenDirectory, "partial.dpapi"), "partial-token");

        try
        {
            GmailDraftService.RestoreArchivedTokenStore(tokenDirectory, archiveDirectory: null);

            Assert.False(Directory.Exists(tokenDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
