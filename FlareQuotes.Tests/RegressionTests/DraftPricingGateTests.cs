using FlareQuotes.App.Services;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class DraftPricingGateTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotCallGmailWhenPricingIsIncomplete()
    {
        var gmail = new TrackingGmailDraftService();
        var workflow = new DraftWorkflowService(gmail, new EmailTemplateService(), new NullLogger());
        var input = new DraftWorkflowInput(
            new QuoteRequest { Email = "customer@example.com" },
            new PricedQuoteResult { Success = false, Message = "Missing price for Power Vent." },
            [],
            "unused.pdf",
            [],
            new AppSettings(),
            "FF60");

        var result = await workflow.ExecuteAsync(input);

        Assert.False(result.Success);
        Assert.Contains("Missing price", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, gmail.CreateDraftCallCount);
    }

    private sealed class TrackingGmailDraftService : IGmailDraftService
    {
        public int CreateDraftCallCount { get; private set; }

        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<EmailDraftResult> CreateDraftAsync(
            EmailDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateDraftCallCount++;
            return Task.FromResult(new EmailDraftResult { Success = true });
        }

        public Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;
        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
