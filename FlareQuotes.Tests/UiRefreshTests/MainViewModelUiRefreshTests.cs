using FlareQuotes.App.Services;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class MainViewModelUiRefreshTests
{
    [Fact]
    public void EditingKeepsOriginalUntilSaveAndReplacesInPlace()
    {
        var viewModel = CreateViewModel();
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = "Living Room";

        viewModel.AddFireplaceCommand.Execute(null);

        var original = Assert.Single(viewModel.Fireplaces);
        viewModel.EditFireplaceCommand.Execute(original);

        Assert.Single(viewModel.Fireplaces);
        Assert.Same(original, viewModel.Fireplaces[0]);
        Assert.True(viewModel.IsEditingFireplace);
        Assert.Equal("Save Changes", viewModel.AddFireplaceButtonText);
        Assert.False(viewModel.CanGeneratePreview);

        viewModel.FireplaceLocation = "Great Room";
        viewModel.AddFireplaceCommand.Execute(null);

        var updated = Assert.Single(viewModel.Fireplaces);
        Assert.NotSame(original, updated);
        Assert.Equal("Great Room", updated.Location);
        Assert.False(viewModel.IsEditingFireplace);
        Assert.Equal("Add Fireplace", viewModel.AddFireplaceButtonText);
        Assert.True(viewModel.CanGeneratePreview);
    }

    [Fact]
    public void PendingSecondFireplaceMustBeAddedBeforePreview()
    {
        var viewModel = CreateViewModel();
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.AddFireplaceCommand.Execute(null);

        Assert.True(viewModel.CanGeneratePreview);

        viewModel.Model = "See Through";
        viewModel.Size = "70";
        viewModel.GlassHeight = "16";

        Assert.True(viewModel.HasPendingNewFireplace);
        Assert.False(viewModel.CanGeneratePreview);
        Assert.Equal("Add the current fireplace to continue", viewModel.ReadinessText);
    }

    private static MainViewModel CreateViewModel()
    {
        var logger = new NullLogger();
        var draftWorkflow = new DraftWorkflowService(new NullGmailDraftService(), new EmailTemplateService(), logger);

        return new MainViewModel(new EmptyParser(), new EmptyFeatureService(), new EmptyMediaService(),
                                 new EmptyPriceBookService(), new EmptyPdfService(), new MemorySettingsService(),
                                 draftWorkflow, logger);
    }

    private sealed class EmptyParser : IQuoteRequestParser
    {
        public QuoteRequest Parse(string rawText) => new();
    }

    private sealed class EmptyFeatureService : IFeatureSelectionService
    {
        public IReadOnlyList<FeatureOption> GetAvailableOptions(FireplaceType type) => [];
        public IReadOnlyList<FeatureOption> DetectFromText(string rawText, FireplaceType type) => [];
    }

    private sealed class EmptyMediaService : IMediaSelectionService
    {
        public IReadOnlyList<MediaOption> GetClassicMedia(FireplaceType type) => [];
        public IReadOnlyList<MediaOption> GetPremiumMedia(FireplaceType type) => [];
        public IReadOnlyList<MediaOption> DetectFromText(string rawText, FireplaceType type) => [];
    }

    private sealed class EmptyPriceBookService : IPriceBookService
    {
        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookWorkbook { SourcePath = path });

        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request,
                                                       CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());

        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
                                                          CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());

        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
                                                             CancellationToken cancellationToken = default) =>
            Task.FromResult(new PricedQuoteResult { Request = request, Success = true });

        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(
            QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceLinkSet>>([]);
    }

    private sealed class EmptyPdfService : IQuotePdfService
    {
        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
                                               CancellationToken cancellationToken = default) =>
            Task.FromResult(outputPath);
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        private AppSettings _settings = new() { PricingFile = "test-pricing.xlsx" };

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class NullGmailDraftService : IGmailDraftService
    {
        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request,
                                                       CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailDraftResult { Success = true });

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
