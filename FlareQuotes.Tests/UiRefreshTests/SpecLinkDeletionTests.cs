using System.Reflection;
using FlareQuotes.App.Services;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class SpecLinkDeletionTests
{
    private const string ProductUrl = "https://flarefireplaces.com/test-product.pdf";
    private const string ManualUrl = "https://flarefireplaces.com/test-custom-guide.pdf";

    [Fact]
    public async Task DuplicateLabelsAndUrlsAreRemovedOnlyFromTheReferencedFireplace()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        AddFireplace(fixture.ViewModel, "Bedroom");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        var originalFirstLink = fixture.ViewModel.SpecLinks[0];
        var originalSecondLink = fixture.ViewModel.SpecLinks[1];
        Assert.Equal(originalFirstLink.Url, originalSecondLink.Url);
        Assert.Equal(originalFirstLink.Label, originalSecondLink.Label);
        Assert.NotEqual(originalFirstLink.FireplaceGroupId, originalSecondLink.FireplaceGroupId);

        var selectedGroup = fixture.ViewModel.UrlVerificationFireplaces[1].GroupId;
        fixture.ViewModel.SelectUrlVerificationFireplaceCommand.Execute(
            fixture.ViewModel.UrlVerificationFireplaces[1]);
        var row = Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows);
        Assert.Same(originalSecondLink, row.SourceLink);

        fixture.ViewModel.RemoveSpecLinkCommand.Execute(row);

        Assert.Same(originalFirstLink, Assert.Single(fixture.ViewModel.SpecLinks));
        Assert.Equal(selectedGroup, fixture.ViewModel.SelectedUrlVerificationFireplace?.GroupId);
        Assert.Empty(fixture.ViewModel.SelectedUrlVerificationRows);
        Assert.Equal(2, fixture.ViewModel.UrlVerificationFireplaces.Count);

        // A stale row must not delete another fireplace's matching URL on a second invocation.
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(row);
        Assert.Same(originalFirstLink, Assert.Single(fixture.ViewModel.SpecLinks));
    }

    [Fact]
    public async Task ManuallyAddedUrlCanBeDeletedWithoutDeletingResolvedLinks()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        var resolvedLink = Assert.Single(fixture.ViewModel.SpecLinks);

        AddManualUrl(fixture.ViewModel);
        var manualRow = Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows,
            row => row.SourceLink?.Status == "manual");

        fixture.ViewModel.RemoveSpecLinkCommand.Execute(manualRow);

        Assert.Same(resolvedLink, Assert.Single(fixture.ViewModel.SpecLinks));
        Assert.DoesNotContain(fixture.ViewModel.SelectedUrlVerificationRows, row => row.Url == ManualUrl);
    }

    [Fact]
    public async Task EmptyFireplaceCardKeepsItsIdentityAndAcceptsAnotherManualUrl()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        AddFireplace(fixture.ViewModel, "Bedroom");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectUrlVerificationFireplaceCommand.Execute(
            fixture.ViewModel.UrlVerificationFireplaces[1]);
        var selectedGroup = fixture.ViewModel.SelectedUrlVerificationFireplace!.GroupId;

        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));
        AddManualUrl(fixture.ViewModel);

        Assert.Equal(2, fixture.ViewModel.UrlVerificationFireplaces.Count);
        Assert.Equal(selectedGroup, fixture.ViewModel.SelectedUrlVerificationFireplace?.GroupId);
        var manualLink = Assert.Single(fixture.ViewModel.SpecLinks, link => link.Status == "manual");
        Assert.Equal(selectedGroup, manualLink.FireplaceGroupId);
        Assert.Equal("Bedroom", manualLink.FireplaceLocation);
        Assert.Equal(ManualUrl, manualLink.Url);
        Assert.Same(manualLink, Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows).SourceLink);
    }

    [Fact]
    public async Task InitiallyEmptyResourceSetStillProvidesACardForAddingManualUrls()
    {
        using var fixture = new Fixture(includeResolvedLinks: false);
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Empty(fixture.ViewModel.SpecLinks);
        var card = Assert.Single(fixture.ViewModel.UrlVerificationFireplaces);
        Assert.Equal("Living Room", card.FireplaceLocation);
        Assert.Empty(fixture.ViewModel.SelectedUrlVerificationRows);

        AddManualUrl(fixture.ViewModel);

        var link = Assert.Single(fixture.ViewModel.SpecLinks);
        Assert.Equal(card.GroupId, link.FireplaceGroupId);
        Assert.Equal(ManualUrl, link.Url);
    }

    [Fact]
    public async Task DraftAfterDeletingEveryUrlDoesNotLookUpOrRestorePricedResourceLinks()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.SeedPricedSnapshot();

        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));
        Assert.Empty(fixture.ViewModel.SpecLinks);

        await fixture.ViewModel.CreateDraftCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Prices.ResolveCallCount);
        Assert.Equal(0, fixture.Pdf.BuildCallCount);
        var draft = Assert.Single(fixture.Gmail.Drafts);
        Assert.DoesNotContain(ProductUrl, draft.HtmlBody, StringComparison.Ordinal);
        Assert.Empty(fixture.ViewModel.SpecLinks);
        Assert.Single(fixture.ViewModel.UrlVerificationFireplaces);
        Assert.Equal(QuoteWorkflowStage.SpecLinks, fixture.ViewModel.WorkflowStage);
    }

    [Fact]
    public async Task SpecLinksCannotChangeDuringDraftCreationAndCanBeEditedAfterFailure()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.SeedPricedSnapshot();
        var row = Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows);
        var originalLink = Assert.Single(fixture.ViewModel.SpecLinks);
        fixture.ViewModel.ManualUrlToolName = "Custom Guide";
        fixture.ViewModel.ManualUrlValue = ManualUrl;
        Assert.True(fixture.ViewModel.AddManualUrlCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.RemoveSpecLinkCommand.CanExecute(row));
        var photoPath = fixture.AddTestPhoto();
        Assert.True(fixture.ViewModel.ChooseFireplacePhotoCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.ClearFireplacePhotoCommand.CanExecute(null));

        var pendingResult = new TaskCompletionSource<EmailDraftResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gmail.PendingDraftResult = pendingResult.Task;
        var draftTask = fixture.ViewModel.CreateDraftCommand.ExecuteAsync(null);

        try
        {
            Assert.Single(fixture.Gmail.Drafts);
            Assert.True(fixture.ViewModel.CreateDraftCommand.IsRunning);
            Assert.False(fixture.ViewModel.AddManualUrlCommand.CanExecute(null));
            Assert.False(fixture.ViewModel.RemoveSpecLinkCommand.CanExecute(row));
            Assert.False(fixture.ViewModel.ChooseFireplacePhotoCommand.CanExecute(null));
            Assert.False(fixture.ViewModel.ClearFireplacePhotoCommand.CanExecute(null));

            // Direct execution also needs to be guarded, even when the UI has disabled the buttons.
            fixture.ViewModel.AddManualUrlCommand.Execute(null);
            fixture.ViewModel.RemoveSpecLinkCommand.Execute(row);
            fixture.ViewModel.ChooseFireplacePhotoCommand.Execute(null);
            fixture.ViewModel.ClearFireplacePhotoCommand.Execute(null);

            Assert.Same(originalLink, Assert.Single(fixture.ViewModel.SpecLinks));
            Assert.Equal("Custom Guide", fixture.ViewModel.ManualUrlToolName);
            Assert.Equal(ManualUrl, fixture.ViewModel.ManualUrlValue);
            Assert.Equal(photoPath, Assert.Single(fixture.ViewModel.FireplacePhotoPaths));
        }
        finally
        {
            pendingResult.TrySetResult(new EmailDraftResult { Success = false, Message = "Test capture only." });
            await draftTask;
        }

        Assert.False(fixture.ViewModel.CreateDraftCommand.IsRunning);
        Assert.True(fixture.ViewModel.AddManualUrlCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.RemoveSpecLinkCommand.CanExecute(row));
        Assert.True(fixture.ViewModel.ChooseFireplacePhotoCommand.CanExecute(null));
        Assert.True(fixture.ViewModel.ClearFireplacePhotoCommand.CanExecute(null));
        fixture.ViewModel.ClearFireplacePhotoCommand.Execute(null);
        Assert.Empty(fixture.ViewModel.FireplacePhotoPaths);
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(row);
        Assert.Empty(fixture.ViewModel.SpecLinks);
        AddManualUrl(fixture.ViewModel);
        Assert.Equal(ManualUrl, Assert.Single(fixture.ViewModel.SpecLinks).Url);
    }

    [Fact]
    public async Task ReturningToSpecReviewKeepsManualAdditionsDeletionsAndSelectedGroup()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        AddFireplace(fixture.ViewModel, "Bedroom");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectUrlVerificationFireplaceCommand.Execute(
            fixture.ViewModel.UrlVerificationFireplaces[1]);
        var selectedGroup = fixture.ViewModel.SelectedUrlVerificationFireplace!.GroupId;
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));
        AddManualUrl(fixture.ViewModel);
        var editedLinks = fixture.ViewModel.SpecLinks.ToArray();

        fixture.ViewModel.BackToPreviewCommand.Execute(null);
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.BackToReviewCommand.Execute(null);
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Prices.ResolveCallCount);
        Assert.Equal(editedLinks, fixture.ViewModel.SpecLinks);
        Assert.Equal(selectedGroup, fixture.ViewModel.SelectedUrlVerificationFireplace?.GroupId);
        Assert.Equal(ManualUrl, Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows).Url);
    }

    [Fact]
    public async Task SavingAnEditedFireplaceInvalidatesPreviousSpecReview()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));
        AddManualUrl(fixture.ViewModel);

        fixture.ViewModel.EditFireplaceCommand.Execute(Assert.Single(fixture.ViewModel.Fireplaces));
        fixture.ViewModel.FireplaceLocation = "Great Room";
        fixture.ViewModel.AddFireplaceCommand.Execute(null);
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.Prices.ResolveCallCount);
        var currentLink = Assert.Single(fixture.ViewModel.SpecLinks);
        Assert.Equal(ProductUrl, currentLink.Url);
        Assert.Equal("Great Room", currentLink.FireplaceLocation);
        Assert.DoesNotContain(fixture.ViewModel.SpecLinks, link => link.Status == "manual");
    }

    [Fact]
    public async Task ChangingPricingWorkbookInvalidatesPreviousSpecReview()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));

        fixture.ViewModel.ApplySettings(new AppSettings
        {
            PricingFile = "updated-test-pricing.xlsx",
            UseGmailSignature = false
        });
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.Prices.ResolveCallCount);
        Assert.Equal(ProductUrl, Assert.Single(fixture.ViewModel.SpecLinks).Url);
    }

    [Fact]
    public async Task ClearingThenStartingANewQuoteDiscardsPreviousReviewAndManualUrls()
    {
        using var fixture = new Fixture();
        AddFireplace(fixture.ViewModel, "Living Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        fixture.ViewModel.RemoveSpecLinkCommand.Execute(
            Assert.Single(fixture.ViewModel.SelectedUrlVerificationRows));
        AddManualUrl(fixture.ViewModel);

        fixture.ViewModel.ClearCommand.Execute(null);

        Assert.Empty(fixture.ViewModel.SpecLinks);
        Assert.Empty(fixture.ViewModel.UrlVerificationFireplaces);
        Assert.Null(fixture.ViewModel.SelectedUrlVerificationFireplace);

        AddFireplace(fixture.ViewModel, "New Project Room");
        await fixture.ViewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.Prices.ResolveCallCount);
        var currentLink = Assert.Single(fixture.ViewModel.SpecLinks);
        Assert.Equal(ProductUrl, currentLink.Url);
        Assert.Equal("New Project Room", currentLink.FireplaceLocation);
        Assert.DoesNotContain(fixture.ViewModel.SpecLinks, link => link.Status == "manual");
    }

    private static void AddFireplace(MainViewModel viewModel, string location)
    {
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = location;
        viewModel.AddFireplaceCommand.Execute(null);
    }

    private static void AddManualUrl(MainViewModel viewModel)
    {
        viewModel.ManualUrlToolName = "Custom Guide";
        viewModel.ManualUrlValue = ManualUrl;
        viewModel.AddManualUrlCommand.Execute(null);
        Assert.Contains(viewModel.SpecLinks, link => link.Status == "manual" && link.Url == ManualUrl);
    }

    private sealed class Fixture : IDisposable
    {
        private string? _scratchPdf;
        private string? _scratchPhoto;

        public Fixture(bool includeResolvedLinks = true)
        {
            Prices = new TrackingPriceBookService(includeResolvedLinks);
            var logger = new NullLogger();
            ViewModel = new MainViewModel(new EmptyParser(), new EmptyFeatureService(), new EmptyMediaService(),
                Prices, Pdf, new MemorySettingsService(),
                new DraftWorkflowService(Gmail, new EmailTemplateService(), logger), logger);
            ViewModel.Email = "customer@example.com";
        }

        public MainViewModel ViewModel { get; }
        public TrackingPriceBookService Prices { get; }
        public TrackingPdfService Pdf { get; } = new();
        public CapturingGmailDraftService Gmail { get; } = new();

        public string AddTestPhoto()
        {
            _scratchPhoto = Path.Combine(Path.GetTempPath(), $"flare-spec-link-photo-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(_scratchPhoto, [137, 80, 78, 71]);
            ViewModel.FireplacePhotoPaths.Add(_scratchPhoto);
            return _scratchPhoto;
        }

        public void SeedPricedSnapshot()
        {
            var request = Prices.LastResolvedRequest!;
            var priced = new PricedQuoteResult
            {
                Request = request,
                Success = true,
                ResourceLinks = Prices.LastResolvedSets,
                Fireplaces = request.Fireplaces.Select(fireplace => new PricedFireplaceQuote
                {
                    ModelNumber = fireplace.Model,
                    FireplaceLocation = fireplace.FireplaceLocation,
                    Type = FireplaceType.Indoor,
                    BaseLine = new PriceLine { Feature = "Fireplace", Price = 1000m }
                }).ToList()
            };
            _scratchPdf = Path.Combine(Path.GetTempPath(), $"flare-spec-link-test-{Guid.NewGuid():N}.pdf");
            File.WriteAllText(_scratchPdf, "%PDF-1.4\n% fake test attachment\n");
            SetPrivateField("_lastRequest", request);
            SetPrivateField("_lastPricedQuote", priced);
            ViewModel.GeneratedPdfPath = _scratchPdf;
        }

        private void SetPrivateField(string name, object value)
        {
            var field = typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(ViewModel, value);
        }

        public void Dispose()
        {
            var cancel = typeof(MainViewModel).GetMethod("CancelEstimatedTotalRefresh",
                BindingFlags.Instance | BindingFlags.NonPublic);
            cancel?.Invoke(ViewModel, null);
            if (_scratchPdf is not null && File.Exists(_scratchPdf))
                File.Delete(_scratchPdf);
            if (_scratchPhoto is not null && File.Exists(_scratchPhoto))
                File.Delete(_scratchPhoto);
        }
    }

    private sealed class TrackingPriceBookService(bool includeResolvedLinks) : IPriceBookService
    {
        public int ResolveCallCount { get; private set; }
        public QuoteRequest? LastResolvedRequest { get; private set; }
        public IReadOnlyList<ResourceLinkSet> LastResolvedSets { get; private set; } = [];

        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookWorkbook { SourcePath = path });

        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());

        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
            CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());

        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PricedQuoteResult { Request = request, Success = true });

        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(QuoteRequest request,
            string pricingPath, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastResolvedRequest = request;
            LastResolvedSets = request.Fireplaces.Select(fireplace =>
            {
                var set = new ResourceLinkSet
                {
                    ModelNumber = fireplace.Model,
                    FireplaceLocation = fireplace.FireplaceLocation
                };
                if (includeResolvedLinks)
                {
                    set.Links["Product Sheet"] = ProductUrl;
                    set.Sources["Product Sheet"] = "specific";
                }
                return set;
            }).ToList();
            return Task.FromResult(LastResolvedSets);
        }
    }

    private sealed class TrackingPdfService : IQuotePdfService
    {
        public int BuildCallCount { get; private set; }

        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
            CancellationToken cancellationToken = default)
        {
            BuildCallCount++;
            return Task.FromResult(outputPath);
        }
    }

    private sealed class CapturingGmailDraftService : IGmailDraftService
    {
        public List<EmailDraftRequest> Drafts { get; } = [];
        public Task<EmailDraftResult>? PendingDraftResult { get; set; }
        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
        public Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            Drafts.Add(request);
            // Keep the quote open without invoking successful-draft history or browser behavior.
            return PendingDraftResult ??
                Task.FromResult(new EmailDraftResult { Success = false, Message = "Test capture only." });
        }
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

    private sealed class MemorySettingsService : ISettingsService
    {
        private AppSettings _settings = new() { PricingFile = "test-pricing.xlsx", UseGmailSignature = false };
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
