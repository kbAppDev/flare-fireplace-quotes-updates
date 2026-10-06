using System.Reflection;
using System.Windows.Threading;
using FlareQuotes.App.Services;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Parsing;
using FlareQuotes.Core.Services;
using Xunit;

namespace FlareQuotes.Tests.AutosaveTests;

public sealed class UnfinishedQuoteAutosaveTests
{
    private const string ProductUrl = "https://flarefireplaces.com/autosave-product.pdf";
    private const string CustomUrl = "https://flarefireplaces.com/autosave-custom.pdf";

    [Fact]
    public async Task RecoveryRestoresCustomerFireplacesAndReviewedUrlsWithoutReusingPdf()
    {
        using var fixture = new Fixture();
        var original = fixture.CreateViewModel();
        original.ClientName = "Autosave Customer";
        original.Email = "autosave@example.com";
        original.ProjectName = "Recovered House";
        original.Postal = "123 Example St";
        AddFireplace(original, "Living Room");
        AddFireplace(original, "Bedroom");
        await original.NextToPreviewCommand.ExecuteAsync(null);
        await original.NextToSpecLinksCommand.ExecuteAsync(null);
        original.SelectUrlVerificationFireplaceCommand.Execute(original.UrlVerificationFireplaces[1]);
        var selectedGroup = original.SelectedUrlVerificationFireplace!.GroupId;
        original.RemoveSpecLinkCommand.Execute(Assert.Single(original.SelectedUrlVerificationRows));
        original.ManualUrlToolName = "Custom Guide";
        original.ManualUrlValue = CustomUrl;
        original.AddManualUrlCommand.Execute(null);
        original.ManualUrlToolName = "Half-entered name";
        original.ManualUrlValue = "https://flarefireplaces.com/half-entered";
        await original.FlushAutosaveAsync();

        var encrypted = File.ReadAllText(fixture.StatePath);
        Assert.DoesNotContain("Autosave Customer", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("autosave@example.com", encrypted, StringComparison.Ordinal);

        var restored = fixture.CreateViewModel();

        Assert.Equal("Autosave Customer", restored.ClientName);
        Assert.Equal("Recovered House", restored.ProjectName);
        Assert.Equal("123 Example St", restored.Postal);
        Assert.Equal(2, restored.Fireplaces.Count);
        Assert.Equal("Bedroom", restored.Fireplaces[1].Location);
        Assert.Equal(selectedGroup, restored.SelectedUrlVerificationFireplace?.GroupId);
        Assert.Equal(CustomUrl, Assert.Single(restored.SelectedUrlVerificationRows).Url);
        Assert.Equal("Half-entered name", restored.ManualUrlToolName);
        Assert.Empty(restored.GeneratedPdfPath);
        Assert.Equal(QuoteWorkflowStage.Review, restored.WorkflowStage);

        var lookupsBeforeRegeneration = fixture.Prices.ResolveCallCount;
        await restored.NextToPreviewCommand.ExecuteAsync(null);
        await restored.NextToSpecLinksCommand.ExecuteAsync(null);
        Assert.Equal(lookupsBeforeRegeneration, fixture.Prices.ResolveCallCount);
        Assert.Equal(CustomUrl, Assert.Single(restored.SelectedUrlVerificationRows).Url);
        Assert.Equal(2, restored.UrlVerificationFireplaces.Count);
    }

    [Fact]
    public async Task EntirelyDeletedSpecListStaysEmptyAfterRecoveryAndPreviewRegeneration()
    {
        using var fixture = new Fixture();
        var original = fixture.CreateViewModel();
        AddFireplace(original, "Living Room");
        await original.NextToSpecLinksCommand.ExecuteAsync(null);
        original.RemoveSpecLinkCommand.Execute(Assert.Single(original.SelectedUrlVerificationRows));
        await original.FlushAutosaveAsync();

        var restored = fixture.CreateViewModel();
        Assert.Empty(restored.SpecLinks);
        var card = Assert.Single(restored.UrlVerificationFireplaces);
        Assert.Equal("Living Room", card.FireplaceLocation);

        await restored.NextToPreviewCommand.ExecuteAsync(null);
        await restored.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Prices.ResolveCallCount);
        Assert.Empty(restored.SpecLinks);
        restored.ManualUrlToolName = "Replacement";
        restored.ManualUrlValue = CustomUrl;
        restored.AddManualUrlCommand.Execute(null);
        Assert.Equal(card.GroupId, Assert.Single(restored.SpecLinks).FireplaceGroupId);
    }

    [Fact]
    public async Task RecoveryRestoresInProgressEditorAndOrderedSelections()
    {
        using var fixture = new Fixture();
        var original = fixture.CreateViewModel();
        AddFireplace(original, "Living Room");
        original.EditFireplaceCommand.Execute(Assert.Single(original.Fireplaces));
        original.FireplaceLocation = "Renamed Room";
        original.FireplaceQuantity = 3;
        original.LeadTime = "6 weeks";
        original.CustomLeadTime = "Pending custom time";
        original.AllFeatureOptions.Single(option => option.Key == "power_vent").IsSelected = true;
        original.AllFeatureOptions.Single(option => option.Key == "rgb_leds").IsSelected = true;
        original.ClassicMediaChoice = original.ClassicMediaOptions.Single(option => option.Key == "fg_silver");
        original.SelectAdditionalClassicMediaCommand.Execute(
            original.AdditionalClassicMediaOptions.Single(option => option.Key == "fg_bronze"));
        original.AllPremiumMediaOptions.Single(option => option.Key == "gold_glass").IsSelected = true;
        var featureKeys = original.SelectedFeatures.Select(feature => feature.Key).ToArray();
        await original.FlushAutosaveAsync();

        var restored = fixture.CreateViewModel();

        Assert.True(restored.IsEditingFireplace);
        Assert.Equal("Living Room", Assert.Single(restored.Fireplaces).Location);
        Assert.Equal("Renamed Room", restored.FireplaceLocation);
        Assert.Equal(3, restored.FireplaceQuantity);
        Assert.Equal("6 weeks", restored.LeadTime);
        Assert.Equal("Pending custom time", restored.CustomLeadTime);
        Assert.Equal(featureKeys, restored.SelectedFeatures.Select(feature => feature.Key));
        Assert.Equal("fg_silver", restored.ClassicMediaChoice?.Key);
        Assert.Equal("fg_bronze", Assert.Single(restored.SelectedAdditionalClassicMedia).Key);
        Assert.Equal("gold_glass", Assert.Single(restored.SelectedPremiumMedia).Key);
        Assert.False(restored.CanGeneratePreview);

        restored.AddFireplaceCommand.Execute(null);
        Assert.Equal("Renamed Room", Assert.Single(restored.Fireplaces).Location);
        Assert.Equal(3, restored.Fireplaces[0].Quantity);
    }

    [Fact]
    public async Task RecoveryKeepsAvailablePhotosAndExplainsMissingFiles()
    {
        using var fixture = new Fixture();
        var availablePhoto = Path.Combine(fixture.Root, "available.png");
        var removedPhoto = Path.Combine(fixture.Root, "removed.png");
        File.WriteAllText(availablePhoto, "test image");
        File.WriteAllText(removedPhoto, "test image");
        var original = fixture.CreateViewModel();
        original.ClientName = "Photo Customer";
        original.FireplacePhotoPaths.Add(availablePhoto);
        original.FireplacePhotoPaths.Add(removedPhoto);
        await original.FlushAutosaveAsync();
        File.Delete(removedPhoto);

        var restored = fixture.CreateViewModel();

        Assert.Equal(availablePhoto, Assert.Single(restored.FireplacePhotoPaths));
        Assert.Contains("1 saved photo attachment(s) are no longer available", restored.StatusMessage,
            StringComparison.Ordinal);
        Assert.Equal("1 fireplace photo selected.", restored.FireplacePhotoSummary);
    }

    [Fact]
    public async Task RecoveryKeepsUnsavedSpecLinkEditorSeparateFromSavedLink()
    {
        using var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        viewModel.Email = "customer@example.com";
        AddFireplace(viewModel, "Living Room");
        await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.SelectedUrlVerificationRows);
        viewModel.EditSpecLinkCommand.Execute(row);
        row.EditingLabel = "New unfinished label";
        row.EditingUrl = CustomUrl;
        await viewModel.FlushAutosaveAsync();

        var restored = fixture.CreateViewModel();
        var restoredRow = Assert.Single(restored.SelectedUrlVerificationRows);
        Assert.True(restoredRow.IsEditing);
        Assert.Equal("New unfinished label", restoredRow.EditingLabel);
        Assert.Equal(CustomUrl, restoredRow.EditingUrl);
        Assert.Equal(ProductUrl, restoredRow.SourceLink?.Url);
        Assert.False(restored.CanCreateGmailDraft);
        restored.SaveSpecLinkCommand.Execute(restoredRow);
        Assert.Equal(CustomUrl, Assert.Single(restored.SpecLinks).Url);
        Assert.True(restored.CanCreateGmailDraft);
    }

    [Fact]
    public async Task ClearAndSuccessfulResetDiscardQueuedRecoveryUntilNextUserEdit()
    {
        using var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        viewModel.ClientName = "First Customer";
        await viewModel.FlushAutosaveAsync();
        Assert.True(File.Exists(fixture.StatePath));

        viewModel.Email = "pending@example.com";
        viewModel.ClearCommand.Execute(null);
        await viewModel.FlushAutosaveAsync();
        Assert.False(File.Exists(fixture.StatePath));
        Assert.Empty(fixture.CreateViewModel().ClientName);

        viewModel.ClientName = "Second Customer";
        await viewModel.FlushAutosaveAsync();
        Assert.True(File.Exists(fixture.StatePath));
        InvokePrivate(viewModel, "ClearQuoteAfterSuccessfulEmail");
        await viewModel.FlushAutosaveAsync();
        Assert.False(File.Exists(fixture.StatePath));

        viewModel.ProjectName = "Next Project";
        await viewModel.FlushAutosaveAsync();
        Assert.Equal("Next Project", fixture.CreateViewModel().ProjectName);
    }

    [Fact]
    public async Task LookupCompletingAfterClearCannotRestoreOldUrlsOrRecoveryState()
    {
        using var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        viewModel.ClientName = "Old Customer";
        AddFireplace(viewModel, "Old Room");
        await viewModel.FlushAutosaveAsync();
        Assert.True(File.Exists(fixture.StatePath));
        var pending = new TaskCompletionSource<IReadOnlyList<ResourceLinkSet>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prices.PendingResourceLinks = pending.Task;
        var lookup = viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        Assert.True(viewModel.NextToSpecLinksCommand.IsRunning);

        viewModel.ClearCommand.Execute(null);
        pending.SetResult([
            new ResourceLinkSet
            {
                ModelNumber = "DVFF60R",
                FireplaceLocation = "Old Room",
                Links = { ["Product Sheet"] = ProductUrl }
            }
        ]);
        await lookup;
        await viewModel.FlushAutosaveAsync();

        Assert.Empty(viewModel.ClientName);
        Assert.Empty(viewModel.Fireplaces);
        Assert.Empty(viewModel.SpecLinks);
        Assert.Empty(viewModel.UrlVerificationFireplaces);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
        Assert.False(File.Exists(fixture.StatePath));
        Assert.Empty(fixture.CreateViewModel().ClientName);

        fixture.Prices.PendingResourceLinks = null;
        AddFireplace(viewModel, "New Room");
        await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        Assert.Equal("New Room", Assert.Single(viewModel.SpecLinks).FireplaceLocation);
    }

    [Fact]
    public async Task RepeatedRecoveryDoesNotDuplicateFireplacesLinksOrPhotos()
    {
        using var fixture = new Fixture();
        var original = fixture.CreateViewModel();
        AddFireplace(original, "Living Room");
        await original.NextToSpecLinksCommand.ExecuteAsync(null);
        await original.FlushAutosaveAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var restored = fixture.CreateViewModel();
            Assert.Single(restored.Fireplaces);
            Assert.Single(restored.SpecLinks);
            Assert.Single(restored.UrlVerificationFireplaces);
            await restored.FlushAutosaveAsync();
        }
    }

    [Fact]
    public void DebounceSavesLatestStateAndClearCancelsPendingWrite()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var fixture = new Fixture();
                var viewModel = fixture.CreateViewModel();
                viewModel.ClientName = "First typed name";
                viewModel.ClientName = "Latest typed name";
                PumpDispatcher(TimeSpan.FromMilliseconds(800));
                Assert.Equal("Latest typed name", fixture.CreateViewModel().ClientName);

                viewModel.Email = "queued@example.com";
                viewModel.ClearCommand.Execute(null);
                PumpDispatcher(TimeSpan.FromMilliseconds(800));
                Assert.False(File.Exists(fixture.StatePath));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The dispatcher autosave check timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public async Task RecallEnrichmentRestoresReviewedLinksAndAttachmentSelections()
    {
        using var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        AddFireplace(viewModel, "Living Room");
        await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
        viewModel.RemoveSpecLinkCommand.Execute(Assert.Single(viewModel.SelectedUrlVerificationRows));
        viewModel.ManualUrlToolName = "Custom Guide";
        viewModel.ManualUrlValue = CustomUrl;
        viewModel.AddManualUrlCommand.Execute(null);
        var photo = Path.Combine(fixture.Root, "recall.png");
        File.WriteAllText(photo, "test photo");
        viewModel.FireplacePhotoPaths.Add(photo);
        var snapshot = new MainViewModel.LastQuoteSnapshot();
        InvokePrivate(viewModel, "EnrichRecallSnapshot", snapshot);
        viewModel.ClearCommand.Execute(null);

        InvokePrivate(viewModel, "RestoreRecallWorkflow", snapshot);

        Assert.Single(viewModel.Fireplaces);
        Assert.Equal(CustomUrl, Assert.Single(viewModel.SpecLinks).Url);
        Assert.Equal(photo, Assert.Single(viewModel.FireplacePhotoPaths));
        Assert.Empty(viewModel.GeneratedPdfPath);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
        await viewModel.FlushAutosaveAsync();
        Assert.True(File.Exists(fixture.StatePath));
    }

    [Fact]
    public async Task CorruptStateAndWriteFailureLeaveQuoteEditingAvailableWithoutRepeatedWarnings()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.StatePath, "invalid protected state");
        var recovered = fixture.CreateViewModel();
        Assert.Equal(1, fixture.Logger.AutosaveWarningCount);
        Assert.Single(fixture.Logger.WarningMessages, message =>
            message.StartsWith("Unfinished quote restore failed:", StringComparison.Ordinal));
        recovered.ClientName = "Can still edit";
        await recovered.FlushAutosaveAsync();
        Assert.Equal("Can still edit", fixture.CreateViewModel().ClientName);
        Assert.Equal(1, fixture.Logger.AutosaveWarningCount);

        var blocker = Path.Combine(fixture.Root, "blocked-folder");
        File.WriteAllText(blocker, "this is a file");
        var failing = fixture.CreateViewModel(Path.Combine(blocker, "unfinished.json.dpapi"));
        failing.ClientName = "Still usable";
        var warningsBeforeSave = fixture.Logger.AutosaveWarningCount;
        await failing.FlushAutosaveAsync();
        await failing.FlushAutosaveAsync();
        Assert.Equal(warningsBeforeSave + 1, fixture.Logger.AutosaveWarningCount);
        Assert.Single(fixture.Logger.WarningMessages, message =>
            message.StartsWith("Unfinished quote save failed:", StringComparison.Ordinal));
        Assert.Equal("Still usable", failing.ClientName);
    }

    private static void AddFireplace(MainViewModel viewModel, string location)
    {
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = location;
        viewModel.AddFireplaceCommand.Execute(null);
    }

    private static void InvokePrivate(MainViewModel viewModel, string name, params object[] args)
    {
        var method = typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, args);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<MainViewModel> _viewModels = [];
        private readonly List<string> _generatedPdfs = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "flare-autosave-tests", Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(Root, "unfinished_quote.json.dpapi");
        public TrackingPriceService Prices { get; } = new();
        public CountingLogger Logger { get; } = new();

        public Fixture() => Directory.CreateDirectory(Root);

        public MainViewModel CreateViewModel(string? statePath = null)
        {
            var viewModel = new MainViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService(),
                new MediaSelectionService(), Prices, new TestPdfService(_generatedPdfs), new MemorySettingsService(),
                new DraftWorkflowService(new TestGmailService(), new EmailTemplateService(), Logger), Logger);
            _viewModels.Add(viewModel);
            InvokePrivate(viewModel, "InitializeAutosaveForTesting", statePath ?? StatePath);
            return viewModel;
        }

        public void Dispose()
        {
            foreach (var viewModel in _viewModels)
                viewModel.ClearUnfinishedQuote();
            foreach (var path in _generatedPdfs)
                File.Delete(path);
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "flare-autosave-tests")) +
                           Path.DirectorySeparatorChar;
            var fixtureRoot = Path.GetFullPath(Root);
            if (!fixtureRoot.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped the autosave test directory.");
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private sealed class TrackingPriceService : IPriceBookService
    {
        public int ResolveCallCount { get; private set; }
        public Task<IReadOnlyList<ResourceLinkSet>>? PendingResourceLinks { get; set; }
        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookWorkbook());
        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());
        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
            CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());
        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new PricedQuoteResult
            {
                Request = request,
                Success = true,
                Fireplaces = request.Fireplaces.Select(fireplace => new PricedFireplaceQuote
                {
                    Type = FireplaceType.Indoor,
                    ModelNumber = "DVFF60R",
                    FireplaceLocation = fireplace.FireplaceLocation,
                    BaseLine = new PriceLine { Price = 1000m }
                }).ToList()
            });

        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(QuoteRequest request, string pricingPath,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            IReadOnlyList<ResourceLinkSet> links = request.Fireplaces.Select(fireplace => new ResourceLinkSet
            {
                ModelNumber = "DVFF60R",
                FireplaceLocation = fireplace.FireplaceLocation,
                Links = { ["Product Sheet"] = ProductUrl },
                Sources = { ["Product Sheet"] = "specific" }
            }).ToList();
            return PendingResourceLinks ?? Task.FromResult(links);
        }
    }

    private sealed class TestPdfService(List<string> generatedPdfs) : IQuotePdfService
    {
        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(outputPath, "%PDF-1.4\n% autosave fixture\n");
            generatedPdfs.Add(outputPath);
            return Task.FromResult(outputPath);
        }
    }

    private sealed class MemorySettingsService : ISettingsService
    {
        private AppSettings _settings = new() { UseGmailSignature = false };
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TestGmailService : IGmailDraftService
    {
        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new EmailDraftResult());
    }

    private sealed class CountingLogger : IAppLogger
    {
        public List<string> WarningMessages { get; } = [];
        public int AutosaveWarningCount => WarningMessages.Count(message =>
            message.StartsWith("Unfinished quote ", StringComparison.Ordinal));
        public string LogFilePath => string.Empty;
        public void Info(string message) { }
        public void Warning(string message) => WarningMessages.Add(message);
        public void Error(Exception exception, string message) { }
    }
}
