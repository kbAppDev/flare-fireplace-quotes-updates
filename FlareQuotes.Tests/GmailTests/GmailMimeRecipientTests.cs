using System.Text;
using FlareQuotes.Core.Models;
using FlareQuotes.Infrastructure.Gmail;
using Xunit;

namespace FlareQuotes.Tests.GmailTests;

public sealed class GmailMimeRecipientTests
{
    [Fact]
    public void BuildsCanonicalToHeaderFromCopiedAddress()
    {
        const string copied = "Test Customer <cus\u200Btomer＠example．com\u00A0>";

        var header = GmailDraftService.BuildAddressHeader(copied, required: true);

        Assert.Equal("customer@example.com", header);
    }

    [Fact]
    public void RawMimeContainsOnlyTheCanonicalRecipient()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"flare-email-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(pdfPath, Encoding.ASCII.GetBytes("%PDF-1.4 test"));

        try
        {
            var raw = GmailDraftService.BuildRawMessage(new EmailDraftRequest
            {
                ToEmail = "mailto:cus\u200Btomer＠example．com.",
                Subject = "Recipient regression test",
                HtmlBody = "<p>Test</p>",
                PdfAttachmentPath = pdfPath,
                OpenBrowserAfterCreate = false
            });

            var mime = Encoding.UTF8.GetString(DecodeBase64Url(raw));

            Assert.StartsWith("To: customer@example.com\r\n", mime, StringComparison.Ordinal);
            Assert.False(mime.Contains("\u200B", StringComparison.Ordinal));
            Assert.False(mime.Contains("＠", StringComparison.Ordinal));
            Assert.False(mime.Contains("．", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void RawMimeBuildHonorsCancellationBeforeAllocatingAttachments()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            GmailDraftService.BuildRawMessage(
                new EmailDraftRequest { ToEmail = "quotes@example.com" }, cancellation.Token));
    }

    [Fact]
    public void RawMimeBuildFailsClosedWhenRequiredQuotePdfIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-quote-{Guid.NewGuid():N}.pdf");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GmailDraftService.BuildRawMessage(new EmailDraftRequest
            {
                ToEmail = "customer@example.com",
                Subject = "Required attachment regression test",
                HtmlBody = "<p>Test</p>",
                PdfAttachmentPath = missingPath
            }));

        Assert.Contains("quote PDF is missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(missingPath, exception.Message, StringComparison.Ordinal);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
