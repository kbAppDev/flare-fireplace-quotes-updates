using FlareQuotes.Core.Security;
using Xunit;

namespace FlareQuotes.Tests.SecurityTests;

public sealed class SensitiveFileDeletionTests
{
    [Fact]
    public void DeletePlaintext_RemovesFileSymlinkWithoutOverwritingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "flare-sensitive-delete-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var target = Path.Combine(root, "target.txt");
            var link = Path.Combine(root, "legacy-token");
            File.WriteAllText(target, "must-remain-intact");
            File.CreateSymbolicLink(link, target);

            SensitiveFileDeletion.DeletePlaintext(link);

            Assert.False(File.Exists(link));
            Assert.Equal("must-remain-intact", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
