using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using FlareQuotes.App.Services;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Email;
using FlareQuotes.Core.Features;
using FlareQuotes.Core.Media;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Parsing;
using FlareQuotes.Core.Services;
using Xunit;

namespace FlareQuotes.Tests.UiRefreshTests;

public sealed class DiscountWorkflowTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("bad")]
    [InlineData("12%")]
    [InlineData("1e2")]
    [InlineData("NaN")]
    [InlineData("999999999999999999999999999999999999")]
    public async Task InvalidTextBlocksCommandsAndDirectExecutionWithoutUsingEarlierValidRate(string invalid)
    {
        var fixture = new Fixture();
        var viewModel = fixture.Create();
        Configure(viewModel, "15");
        viewModel.Email = "customer@example.com";
        viewModel.DiscountPercentText = invalid;

        Assert.False(viewModel.AddFireplaceCommand.CanExecute(null));
        Assert.False(viewModel.NextToPreviewCommand.CanExecute(null));
        Assert.False(viewModel.CreateDraftCommand.CanExecute(null));
        viewModel.AddFireplaceCommand.Execute(null);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        await viewModel.CreateDraftCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Fireplaces);
        Assert.Equal(invalid, viewModel.DiscountPercentText);
        Assert.Contains("0 to 100", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Prices.BuildCount);
        Assert.Equal(0, fixture.Pdf.BuildCount);
        Assert.Equal(0, fixture.Gmail.BuildCount);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("12.5", 12.5)]
    [InlineData("100", 100)]
    public void ValidRatesAreSavedAndClearCurrentPreservesQuoteTerms(string entered, double expected)
    {
        var viewModel = new Fixture().Create();
        viewModel.DiscountTerms = "Payment required before shipment.";
        Configure(viewModel, entered);
        viewModel.AddFireplaceCommand.Execute(null);
        Assert.Equal((decimal)expected, Assert.Single(viewModel.Fireplaces).DiscountPercent);
        Assert.Equal(string.Empty, viewModel.DiscountPercentText);
        Assert.Equal("Payment required before shipment.", viewModel.DiscountTerms);
    }

    [Fact]
    public async Task SeparateRatesSurviveDuplicateEditAndRequestCreation()
    {
        var fixture = new Fixture();
        var viewModel = fixture.Create();
        viewModel.DiscountTerms = "Project discount.\nValid until October 31.";
        Configure(viewModel, "12.5");
        viewModel.AddFireplaceCommand.Execute(null);
        Configure(viewModel, "7.25");
        viewModel.AddFireplaceCommand.Execute(null);
        var original = viewModel.Fireplaces[0];
        viewModel.DuplicateFireplaceCommand.Execute(original);
        Assert.Equal(12.5m, viewModel.Fireplaces[1].DiscountPercent);
        Assert.Equal(12.5m.ToString(CultureInfo.CurrentCulture), viewModel.DiscountPercentText);
        viewModel.DiscountPercentText = "30";
        viewModel.AddFireplaceCommand.Execute(null);

        Assert.Equal(new[] { 12.5m, 30m, 7.25m }, viewModel.Fireplaces.Select(item => item.DiscountPercent));
        Assert.Equal(12.5m, original.DiscountPercent);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.NotNull(fixture.Prices.LastRequest);
        Assert.Equal(new[] { 12.5m, 30m, 7.25m }, fixture.Prices.LastRequest.Fireplaces.Select(item => item.DiscountPercent));
        Assert.Equal(viewModel.DiscountTerms, fixture.Prices.LastRequest.DiscountTerms);
    }

    [Fact]
    public async Task UnsavedDiscountEditCannotPreviewOrDraftTheEarlierSavedRate()
    {
        var fixture = new Fixture();
        var viewModel = fixture.Create();
        Configure(viewModel, "10");
        viewModel.AddFireplaceCommand.Execute(null);
        viewModel.EditFireplaceCommand.Execute(Assert.Single(viewModel.Fireplaces));
        viewModel.DiscountPercentText = "25";
        viewModel.Email = "customer@example.com";
        Assert.False(viewModel.CanGeneratePreview);
        Assert.False(viewModel.CanCreateGmailDraft);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        await viewModel.CreateDraftCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Prices.BuildCount);
        Assert.Equal(0, fixture.Gmail.BuildCount);
        Assert.Equal(10m, Assert.Single(viewModel.Fireplaces).DiscountPercent);
    }

    [Fact]
    public async Task DiscountChangesInvalidatePdfAndPreviewUsesDiscountedAmounts()
    {
        var fixture = new Fixture();
        var viewModel = fixture.Create();
        Configure(viewModel, "12.5");
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.NotEmpty(viewModel.GeneratedPdfPath);
        var preview = Assert.Single(viewModel.QuotePreviewRows);
        Assert.Equal(875m.ToString("C2"), preview.BasePrice);
        Assert.Equal(975m.ToString("C2"), preview.TotalPrice);
        viewModel.DiscountTerms = "New terms";
        Assert.Empty(viewModel.GeneratedPdfPath);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.Equal("New terms", fixture.Pdf.LastRequest!.DiscountTerms);
        viewModel.DiscountPercentText = "25";
        Assert.Empty(viewModel.GeneratedPdfPath);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
    }

    [Fact]
    public async Task DiscountChangesDuringPricingCannotPublishTheStalePreview()
    {
        var fixture = new Fixture();
        var pending = new TaskCompletionSource<PricedQuoteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prices.Pending = pending.Task;
        var viewModel = fixture.Create();
        Configure(viewModel, "10");
        var preview = viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Prices.BuildCount);
        viewModel.DiscountPercentText = "bad";
        pending.SetResult(new PricedQuoteResult { Success = true });
        await preview;
        Assert.Empty(viewModel.GeneratedPdfPath);
        Assert.Equal(0, fixture.Pdf.BuildCount);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
    }

    [Fact]
    public async Task RemovingSavedFireplaceDuringPricingCannotPublishTheOldQuote()
    {
        var fixture = new Fixture();
        var pending = new TaskCompletionSource<PricedQuoteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prices.Pending = pending.Task;
        var viewModel = fixture.Create();
        Configure(viewModel, "10");
        viewModel.AddFireplaceCommand.Execute(null);
        var saved = Assert.Single(viewModel.Fireplaces);
        var preview = viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Prices.BuildCount);
        viewModel.RemoveFireplaceCommand.Execute(saved);
        pending.SetResult(new PricedQuoteResult { Success = true });
        await preview;
        Assert.Empty(viewModel.Fireplaces);
        Assert.Empty(viewModel.GeneratedPdfPath);
        Assert.Equal(0, fixture.Pdf.BuildCount);
        Assert.Contains("Quote details changed", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuoteEditingCommandsCannotChangeDiscountedRequestWhileDraftCreationIsPending()
    {
        var fixture = new Fixture();
        var pending = new TaskCompletionSource<EmailDraftResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gmail.Pending = pending.Task;
        var viewModel = fixture.Create();
        Configure(viewModel, "12.5");
        viewModel.AddFireplaceCommand.Execute(null);
        Configure(viewModel, "7");
        viewModel.AddFireplaceCommand.Execute(null);
        var first = viewModel.Fireplaces[0];
        var second = viewModel.Fireplaces[1];
        viewModel.Email = "customer@example.com";
        viewModel.DiscountTerms = "Original quote terms";
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        var pdfPath = viewModel.GeneratedPdfPath;
        File.WriteAllText(pdfPath, "%PDF-1.4\n% test fixture\n");
        try
        {
            await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
            (System.Windows.Input.ICommand Command, object? Argument)[] edits =
            [
                (viewModel.BackToReviewCommand, null), (viewModel.BackToPreviewCommand, null),
                (viewModel.ClearCommand, null), (viewModel.RecallLastQuoteCommand, null),
                (viewModel.RecallQuoteCommand, new MainViewModel.LastQuoteSnapshot { DiscountTerms = "Wrong terms" }),
                (viewModel.EditFireplaceCommand, first), (viewModel.RemoveFireplaceCommand, first),
                (viewModel.DuplicateFireplaceCommand, first), (viewModel.MoveFireplaceUpCommand, second),
                (viewModel.MoveFireplaceDownCommand, first), (viewModel.AddFireplaceCommand, null),
                (viewModel.ClearCurrentFireplaceCommand, null), (viewModel.IncreaseFireplaceQuantityCommand, null),
                (viewModel.DecreaseFireplaceQuantityCommand, null), (viewModel.ClearFeaturesCommand, null),
                (viewModel.ClearPremiumMediaCommand, null), (viewModel.ClearClassicMediaCommand, null),
                (viewModel.ClearAdditionalClassicMediaCommand, null), (viewModel.ClearLeadTimeCommand, null),
                (viewModel.SelectLeadTimeCommand, "Wrong lead time")
            ];
            var notifications = 0;
            foreach (var edit in edits)
                edit.Command.CanExecuteChanged += (_, _) => notifications++;
            var draft = viewModel.CreateDraftCommand.ExecuteAsync(null);
            Assert.True(viewModel.CreateDraftCommand.IsRunning);
            Assert.False(viewModel.CanEditQuote);
            Assert.Equal(1, fixture.Gmail.BuildCount);
            foreach (var edit in edits)
            {
                Assert.False(edit.Command.CanExecute(edit.Argument));
                edit.Command.Execute(edit.Argument);
            }
            Assert.False(viewModel.MoveFireplace(first, second));
            Assert.False(viewModel.AutoFillCommand.CanExecute(null));
            Assert.False(viewModel.NextToPreviewCommand.CanExecute(null));
            Assert.False(viewModel.NextToSpecLinksCommand.CanExecute(null));
            await viewModel.AutoFillCommand.ExecuteAsync(null);
            await viewModel.NextToPreviewCommand.ExecuteAsync(null);
            await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);
            Assert.Equal(QuoteWorkflowStage.SpecLinks, viewModel.WorkflowStage);
            Assert.Equal("Original quote terms", viewModel.DiscountTerms);
            Assert.Same(first, viewModel.Fireplaces[0]);
            Assert.Same(second, viewModel.Fireplaces[1]);
            Assert.Equal(new[] { 12.5m, 7m }, viewModel.Fireplaces.Select(item => item.DiscountPercent));
            Assert.False(viewModel.IsEditingFireplace);
            Assert.Equal(pdfPath, viewModel.GeneratedPdfPath);
            Assert.Equal(1, fixture.Prices.BuildCount);
            pending.SetResult(new EmailDraftResult { Success = false, Message = "Test draft finished." });
            await draft;
            Assert.True(viewModel.CanEditQuote);
            Assert.True(viewModel.ClearCommand.CanExecute(null));
            Assert.True(viewModel.EditFireplaceCommand.CanExecute(first));
            Assert.True(notifications >= edits.Length * 2);
        }
        finally
        {
            pending.TrySetResult(new EmailDraftResult { Success = false });
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void AutosaveSerializationPreservesInvalidEditorAndGlobalTermsThenClearResetsThem()
    {
        var viewModel = new Fixture().Create();
        viewModel.DiscountTerms = "First line\nSecond line";
        Configure(viewModel, "12.345");
        viewModel.AddFireplaceCommand.Execute(null);
        viewModel.EditFireplaceCommand.Execute(Assert.Single(viewModel.Fireplaces));
        viewModel.DiscountPercentText = "12..5";
        var state = (MainViewModel.UnfinishedQuoteState)Invoke(viewModel, "CaptureUnfinishedQuoteState")!;
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        var restored = new Fixture().Create();
        Invoke(restored, "RestoreUnfinishedQuoteState", JsonSerializer.Deserialize<MainViewModel.UnfinishedQuoteState>(json)!);
        Assert.Equal("12..5", restored.DiscountPercentText);
        Assert.True(restored.HasInvalidDiscount);
        Assert.True(restored.IsEditingFireplace);
        Assert.Equal(12.345m, Assert.Single(restored.Fireplaces).DiscountPercent);
        Assert.Equal($"{12.345m:G29}% off base fireplace", restored.Fireplaces[0].DiscountDisplay);
        Assert.Equal("First line\nSecond line", restored.DiscountTerms);
        restored.ClearCurrentFireplaceCommand.Execute(null);
        Assert.Equal("First line\nSecond line", restored.DiscountTerms);
        Assert.Empty(restored.DiscountPercentText);
        Assert.Equal(12.345m, Assert.Single(restored.Fireplaces).DiscountPercent);
        restored.ClearCommand.Execute(null);
        Assert.Empty(restored.DiscountTerms);
        Assert.Empty(restored.DiscountPercentText);
        Assert.Empty(restored.Fireplaces);
    }

    [Fact]
    public void LegacyRecoveryDefaultsToNoDiscountAndRecallRestoresIndependentRates()
    {
        var viewModel = new Fixture().Create();
        var legacy = JsonSerializer.Deserialize<MainViewModel.UnfinishedQuoteState>(
            "{\"Model\":\"Front Facing\",\"Size\":\"60\",\"GlassHeight\":\"16\"}")!;
        Invoke(viewModel, "RestoreUnfinishedQuoteState", legacy);
        Assert.False(viewModel.HasInvalidDiscount);
        Assert.Empty(viewModel.DiscountTerms);
        Assert.Empty(viewModel.DiscountPercentText);
        var recalled = new MainViewModel.LastQuoteSnapshot
        {
            DiscountTerms = "Recall terms",
            Fireplaces = [new FireplaceQuoteDraft { Model = "Front Facing", Size = "60", GlassHeight = "16", DiscountPercent = 15.75m }]
        };
        viewModel.RecallQuoteCommand.Execute(recalled);
        Assert.Equal("Recall terms", viewModel.DiscountTerms);
        Assert.Equal(15.75m, Assert.Single(viewModel.Fireplaces).DiscountPercent);
        viewModel.EditFireplaceCommand.Execute(viewModel.Fireplaces[0]);
        Assert.Equal(15.75m.ToString(CultureInfo.CurrentCulture), viewModel.DiscountPercentText);
        Invoke(viewModel, "ClearQuoteAfterSuccessfulEmail");
        Assert.Empty(viewModel.DiscountTerms);
        Assert.Empty(viewModel.DiscountPercentText);
        Assert.Empty(viewModel.Fireplaces);
    }

    [Fact]
    public async Task InvalidPersistedRateBlocksPreviewAndCanBeRepairedByEditing()
    {
        var fixture = new Fixture();
        var viewModel = fixture.Create();
        var saved = new FireplaceQuoteDraft { Model = "Front Facing", Size = "60", GlassHeight = "16", DiscountPercent = 101m };
        viewModel.Fireplaces.Add(saved);
        Assert.False(viewModel.CanGeneratePreview);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Prices.BuildCount);
        viewModel.EditFireplaceCommand.Execute(saved);
        Assert.False(viewModel.AddFireplaceCommand.CanExecute(null));
        viewModel.DiscountPercentText = "5";
        viewModel.AddFireplaceCommand.Execute(null);
        Assert.True(viewModel.CanGeneratePreview);
        Assert.Equal(5m, Assert.Single(viewModel.Fireplaces).DiscountPercent);
    }

    [Fact]
    public void DiscountFieldsHaveAccessibleLabelsAndImmediateTextBindings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FlareQuotes.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "FlareQuotes.App", "Views", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var settings = Assert.Single(document.Descendants(), item => (string?)item.Attribute("Click") == "SettingsButton_Click");
        Assert.Equal("{Binding CanEditQuote}", (string?)settings.Attribute("IsEnabled"));
        foreach (var (name, label) in new[] { ("DiscountPercentBox", "Base fireplace discount percent"), ("DiscountTermsBox", "Quote discount terms") })
        {
            var element = Assert.Single(document.Descendants(), item => (string?)item.Attribute(xaml + "Name") == name);
            Assert.Equal(label, (string?)element.Attribute("AutomationProperties.Name"));
            Assert.Contains("UpdateSourceTrigger=PropertyChanged", (string?)element.Attribute("Text"));
        }
    }

    private static void Configure(MainViewModel viewModel, string discount)
    {
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.DiscountPercentText = discount;
    }

    private static object? Invoke(MainViewModel viewModel, string name, params object[] arguments) =>
        typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewModel, arguments);

    private sealed class Fixture
    {
        public TrackingPrices Prices { get; } = new();
        public TrackingPdf Pdf { get; } = new();
        public TrackingGmail Gmail { get; } = new();
        public MainViewModel Create()
        {
            var logger = new NullLogger();
            return new MainViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService(),
                new MediaSelectionService(), Prices, Pdf, new Settings(),
                new DraftWorkflowService(Gmail, new EmailTemplateService(), logger), logger);
        }
    }

    private sealed class TrackingPrices : IPriceBookService
    {
        public int BuildCount { get; private set; }
        public QuoteRequest? LastRequest { get; private set; }
        public Task<PricedQuoteResult>? Pending { get; set; }
        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookWorkbook());
        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());
        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature, CancellationToken cancellationToken = default) => Task.FromResult(new PriceBookMatch());
        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default)
        {
            BuildCount++;
            LastRequest = request;
            return Pending ?? Task.FromResult(new PricedQuoteResult
            {
                Request = request,
                Success = true,
                Fireplaces = request.Fireplaces.Select(item => new PricedFireplaceQuote
                {
                    Model = item.Model,
                    ModelNumber = "DVFF60R",
                    DiscountPercent = item.DiscountPercent,
                    DiscountEligibleMsrp = 1000m,
                    BaseLine = new PriceLine { Price = 1000m },
                    OptionalFeatures = [new PriceLine { Price = 100m }]
                }).ToList()
            });
        }
        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ResourceLinkSet>>([]);
    }

    private sealed class TrackingPdf : IQuotePdfService
    {
        public int BuildCount { get; private set; }
        public QuoteRequest? LastRequest { get; private set; }
        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath, CancellationToken cancellationToken = default)
        {
            BuildCount++;
            LastRequest = request;
            return Task.FromResult(Path.Join(Path.GetTempPath(), $"flare-discount-workflow-{Guid.NewGuid():N}.pdf"));
        }
    }

    private sealed class Settings : ISettingsService
    {
        private AppSettings _settings = new() { PricingFile = "test-pricing.xlsx" };
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingGmail : IGmailDraftService
    {
        public int BuildCount { get; private set; }
        public Task<EmailDraftResult>? Pending { get; set; }
        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request, CancellationToken cancellationToken = default)
        {
            BuildCount++;
            return Pending ?? Task.FromResult(new EmailDraftResult());
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
