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
            var plaintextPath = Path.Combine(root, "legacy-google-token");
            await File.WriteAllTextAsync(plaintextPath, "{\"access_token\":\"secret-marker\"}");
            var store = new ProtectedFileDataStore(root);

            var token = await store.GetAsync<TestToken>("user");

            Assert.Equal("secret-marker", token.AccessToken);
            Assert.False(File.Exists(plaintextPath));
            var protectedPath = Assert.Single(Directory.GetFiles(root, "*.dpapi"));
            Assert.DoesNotContain("secret-marker", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(protectedPath)));
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
