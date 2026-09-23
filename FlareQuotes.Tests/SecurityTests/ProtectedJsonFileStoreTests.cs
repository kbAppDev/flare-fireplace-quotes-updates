using System.Text;
using System.Text.Json;
using FlareQuotes.Core.Security;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class ProtectedJsonFileStoreTests
{
    [Fact]
    public void SavesEncryptedJsonAndLoadsItForCurrentWindowsUser()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "history.json.dpapi");
            var store = new ProtectedJsonFileStore("Quote History Test");
            var source = new TestRecord { Email = "customer-marker@example.com", Project = "Test Project" };

            store.Save(path, source);
            var raw = File.ReadAllBytes(path);
            var loaded = store.LoadOrMigrate<TestRecord>(path);

            Assert.DoesNotContain("customer-marker@example.com", Encoding.UTF8.GetString(raw));
            Assert.NotNull(loaded);
            Assert.Equal(source.Email, loaded!.Email);
            Assert.Equal(source.Project, loaded!.Project);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesAndDeletesLegacyPlaintextJson()
    {
        var root = CreateTempDirectory();
        try
        {
            var protectedPath = Path.Combine(root, "history.json.dpapi");
            var legacyPath = Path.Combine(root, "history.json");
            var source = new TestRecord { Email = "legacy-marker@example.com", Project = "Legacy Project" };
            File.WriteAllText(legacyPath, JsonSerializer.Serialize(source));

            var store = new ProtectedJsonFileStore("Quote History Migration Test");
            var loaded = store.LoadOrMigrate<TestRecord>(protectedPath, legacyPath);

            Assert.NotNull(loaded);
            Assert.Equal(source.Email, loaded!.Email);
            Assert.Equal(source.Project, loaded!.Project);
            Assert.True(File.Exists(protectedPath));
            Assert.False(File.Exists(legacyPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VerifiedProtectedJsonRemovesAdditionalLegacyPlaintextCopies()
    {
        var root = CreateTempDirectory();
        try
        {
            var protectedPath = Path.Combine(root, "history.json.dpapi");
            var copiedLegacyPath = Path.Combine(root, "history.json");
            var oldAppRoot = Path.Combine(root, "old-app");
            var oldAppLegacyPath = Path.Combine(oldAppRoot, "recent_quotes.json");
            Directory.CreateDirectory(oldAppRoot);
            var source = new TestRecord { Email = "protected@example.com", Project = "Protected Project" };
            var store = new ProtectedJsonFileStore("Quote History Legacy Cleanup Test");
            store.Save(protectedPath, source);
            File.WriteAllText(copiedLegacyPath, JsonSerializer.Serialize(source));
            File.WriteAllText(oldAppLegacyPath, JsonSerializer.Serialize(source));

            var loaded = store.LoadOrMigrate<TestRecord>(protectedPath, copiedLegacyPath,
                                                         additionalLegacyPlaintextPaths: [oldAppLegacyPath]);

            Assert.NotNull(loaded);
            Assert.Equal(source.Email, loaded!.Email);
            Assert.False(File.Exists(copiedLegacyPath));
            Assert.False(File.Exists(oldAppLegacyPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "flare-protected-json-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public sealed class TestRecord
    {
        public string Email { get; set; } = string.Empty;
        public string Project { get; set; } = string.Empty;
    }
}
