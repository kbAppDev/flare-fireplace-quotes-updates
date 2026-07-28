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
}
