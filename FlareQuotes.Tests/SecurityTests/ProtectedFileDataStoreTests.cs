using System.Text;
using FlareQuotes.Infrastructure.Gmail;
using Newtonsoft.Json;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class ProtectedFileDataStoreTests
{
    [Fact]
    public async Task MigratesAndRemovesLegacyPlaintextToken()
    {
        var root = CreateTempDirectory();
        try
        {
            var protectedRoot = Path.Combine(root, "GmailToken");
            var legacyRoot = Path.Combine(root, "GoogleToken");
            Directory.CreateDirectory(legacyRoot);
            var plaintextPath = Path.Combine(legacyRoot, $"{typeof(TestToken).FullName}-user");
            await File.WriteAllTextAsync(plaintextPath, "{\"access_token\":\"secret-marker\"}");
            var store = new ProtectedFileDataStore(protectedRoot, [legacyRoot], []);

            var token = await store.GetAsync<TestToken>("user");

            Assert.Equal("secret-marker", token.AccessToken);
            Assert.False(File.Exists(plaintextPath));
            var protectedPath = Assert.Single(Directory.GetFiles(protectedRoot, "*.dpapi"));
            Assert.DoesNotContain("secret-marker", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(protectedPath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ObsoleteCertBuilderTokenIsNeverImportedAndIsRemovedAfterProtectedGmailTokenExists()
    {
        var root = CreateTempDirectory();
        try
        {
            var protectedRoot = Path.Combine(root, "GmailToken");
            var obsoleteRoot = Path.Combine(root, "GoogleTokenCertBuilder");
            Directory.CreateDirectory(obsoleteRoot);
            var obsoletePath = Path.Combine(obsoleteRoot, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-cert-builder-user");
            await File.WriteAllTextAsync(obsoletePath, "{\"access_token\":\"obsolete-marker\"}");
            var store = new ProtectedFileDataStore(protectedRoot, [], [obsoleteRoot]);

            Assert.Null(await store.GetAsync<TestToken>("user"));
            Assert.True(File.Exists(obsoletePath));

            await store.StoreAsync("user", new TestToken { AccessToken = "gmail-marker" });

            Assert.False(File.Exists(obsoletePath));
            Assert.Equal("gmail-marker", (await store.GetAsync<TestToken>("user")).AccessToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReconnectModeDoesNotReuseLegacyGmailTokenButCleansItAfterNewTokenIsProtected()
    {
        var root = CreateTempDirectory();
        try
        {
            var protectedRoot = Path.Combine(root, "GmailToken");
            var legacyRoot = Path.Combine(root, "GoogleToken");
            Directory.CreateDirectory(legacyRoot);
            var legacyPath = Path.Combine(legacyRoot, $"{typeof(TestToken).FullName}-user");
            await File.WriteAllTextAsync(legacyPath, "{\"access_token\":\"old-account-marker\"}");
            var store = new ProtectedFileDataStore(protectedRoot, [legacyRoot], [], allowLegacyMigration: false);

            Assert.Null(await store.GetAsync<TestToken>("user"));
            Assert.True(File.Exists(legacyPath));

            await store.StoreAsync("user", new TestToken { AccessToken = "new-account-marker" });

            Assert.False(File.Exists(legacyPath));
            Assert.Equal("new-account-marker", (await store.GetAsync<TestToken>("user")).AccessToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClearRemovesProtectedAndLegacyFilesFromOwnedStore()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProtectedFileDataStore(root);
            await store.StoreAsync("user", new TestToken { AccessToken = "secret-marker" });
            await File.WriteAllTextAsync(Path.Combine(root, "leftover.migrated"),
                                         "{\"refresh_token\":\"legacy-marker\"}");

            await store.ClearAsync();

            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "flare-token-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestToken
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; } = string.Empty;
    }
}
