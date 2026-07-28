using FlareQuotes.App.Services;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Parsing;
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



    [Theory]
    [InlineData("DVFF50HC")]
    [InlineData("DVST80EC")]
    public async Task DiscontinuedCommercialCodeAutoFillIsBlocked(string commercialCode)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser());
        viewModel.RawRequest = commercialCode;

        await viewModel.AutoFillCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrWhiteSpace(viewModel.Model));
        Assert.True(string.IsNullOrWhiteSpace(viewModel.Size));
        Assert.True(string.IsNullOrWhiteSpace(viewModel.GlassHeight));
        Assert.Empty(viewModel.Fireplaces);
        Assert.False(viewModel.CanGeneratePreview);
        Assert.Contains("discontinued", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompleteVentFreeDoubleCornerCodeRemainsDoubleCorner()
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser());

        viewModel.Model = "VFDC50H";

        Assert.Equal("Outdoor Vent Free Double Corner", viewModel.Model);
        Assert.Equal("50", viewModel.Size);
        Assert.Equal("24", viewModel.GlassHeight);

        viewModel.AddFireplaceCommand.Execute(null);

        var fireplace = Assert.Single(viewModel.Fireplaces);
        Assert.Equal("VDC50H", fireplace.Model);
    }

    [Fact]
    public void VfdcAndVdcExposeTheSameDoubleCornerOptionalFeatures()
    {
        var vdc = CreateViewModel(featureService: new FeatureSelectionService());
        vdc.Model = "VDC50H";
        vdc.Size = "50";
        vdc.GlassHeight = "24";

        var vfdc = CreateViewModel(featureService: new FeatureSelectionService());
        vfdc.Model = "VFDC50H";
        vfdc.Size = "50";
        vfdc.GlassHeight = "24";

        var vdcFeatures = vdc.AllFeatureOptions.Select(option => option.Key).OrderBy(key => key).ToList();
        var vfdcFeatures = vfdc.AllFeatureOptions.Select(option => option.Key).OrderBy(key => key).ToList();

        Assert.Equal(vdcFeatures, vfdcFeatures);
        Assert.DoesNotContain(vfdcFeatures,
                              key => string.Equals(key, "reflective_black_sides",
                                                   StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FireplaceCardsCanBeReorderedWithoutChangingTheirContents()
    {
        var viewModel = CreateViewModel();
        var first = new FireplaceQuoteDraft { FireplaceLabel = "First", Model = "FF50", Size = "50" };
        var second = new FireplaceQuoteDraft { FireplaceLabel = "Second", Model = "ST60", Size = "60" };
        var third = new FireplaceQuoteDraft { FireplaceLabel = "Third", Model = "VDC70", Size = "70" };

        viewModel.Fireplaces.Add(first);
        viewModel.Fireplaces.Add(second);
        viewModel.Fireplaces.Add(third);

        Assert.True(viewModel.MoveFireplace(third, first));

        Assert.Same(third, viewModel.Fireplaces[0]);
        Assert.Same(first, viewModel.Fireplaces[1]);
        Assert.Same(second, viewModel.Fireplaces[2]);
        Assert.Equal("Third", viewModel.Fireplaces[0].FireplaceLabel);
        Assert.Contains("position 1", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UrlVerificationCreatesOneCardPerResourceSetInstance()
    {
        var viewModel = CreateViewModel();

        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "001:VDC50H",
                FireplaceCode = "VDC50H",
                Label = "3-Part Spec",
                Url = "https://example.com/vdc-3-part.docx",
                Status = "specific"
            });
        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "001:VDC50H",
                FireplaceCode = "VDC50H",
                Label = "Product Sheet",
                Url = "https://example.com/vdc-product.pdf",
                Status = "specific"
            });
        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "002:FF140H",
                FireplaceCode = "FF140H",
                Label = "3-Part Spec",
                Url = "https://example.com/ff140-3-part.docx",
                Status = "specific"
            });
        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "002:FF140H",
                FireplaceCode = "FF140H",
                Label = "Product Sheet",
                Url = "https://example.com/ff140-product.pdf",
                Status = "specific"
            });

        Assert.Equal(2, viewModel.UrlVerificationFireplaces.Count);
        Assert.Equal("VDC50H", viewModel.UrlVerificationFireplaces[0].ModelCode);
        Assert.Equal("FF140H", viewModel.UrlVerificationFireplaces[1].ModelCode);
        Assert.Equal(2, viewModel.UrlVerificationFireplaces[0].Rows.Count);
        Assert.Equal(2, viewModel.UrlVerificationFireplaces[1].Rows.Count);
    }

    [Fact]
    public void GmailDraftButtonRequiresOneValidRecipient()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanCreateGmailDraft);
        Assert.False(viewModel.CreateDraftCommand.CanExecute(null));
        Assert.Contains("valid customer email", viewModel.GmailDraftRequirementText, StringComparison.OrdinalIgnoreCase);

        viewModel.Email = "dealer@example.com";

        Assert.True(viewModel.CanCreateGmailDraft);
        Assert.True(viewModel.CreateDraftCommand.CanExecute(null));
        Assert.Contains("dealer@example.com", viewModel.GmailDraftRequirementText, StringComparison.OrdinalIgnoreCase);
    }

    private static MainViewModel CreateViewModel(
        IQuoteRequestParser? parser = null,
        IFeatureSelectionService? featureService = null)
    {
        var logger = new NullLogger();
        var draftWorkflow = new DraftWorkflowService(new NullGmailDraftService(), new EmailTemplateService(), logger);

        return new MainViewModel(parser ?? new EmptyParser(), featureService ?? new EmptyFeatureService(),
                                 new EmptyMediaService(), new EmptyPriceBookService(), new EmptyPdfService(),
                                 new MemorySettingsService(), draftWorkflow, logger);
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

        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) =>
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
