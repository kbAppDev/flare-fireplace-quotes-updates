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

public sealed class TraditionalOptionsUiTests
{
    [Theory]
    [InlineData("TR-42", "Traditional", "42")]
    [InlineData("TR-46", "Traditional", "46")]
    [InlineData("TRA-BON-42", "Traditional Bonfire", "42")]
    [InlineData("TRA-BON-46", "Traditional Bonfire", "46")]
    public void TraditionalOptionsAreVisibleSelectableAndRetainedThroughSaveAndEdit(
        string modelCode, string modelName, string size)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["active_heat_flex"] = "Active Heat Flex",
            ["air_intake_louver"] = "Air Intake Louver",
            ["analog_wall_timer"] = "Analog Wall Timer",
            ["heat_release_louver"] = "Free Flow Heat Release Louver",
            ["offset_light_stone_brick_traditional"] = "Offset Light Stone Brick Traditional",
            ["passive_heat_flex"] = "Passive Heat Flex"
        };
        var viewModel = CreateViewModel();
        viewModel.Model = modelCode;
        Assert.Equal(modelName, viewModel.Model);
        Assert.Equal(size, viewModel.Size);

        // Fixed expected keys prevent a missing catalog option from defining its own expected result.
        foreach (var (key, label) in expected)
        {
            var option = Assert.Single(viewModel.AllFeatureOptions, item => item.Key == key);
            Assert.Equal(label, option.DisplayName);
            Assert.Contains(viewModel.FilteredFeatureOptions, item => ReferenceEquals(item, option));
            Assert.True(viewModel.ToggleFeatureCommand.CanExecute(option));
            viewModel.ToggleFeatureCommand.Execute(option);
            Assert.True(option.IsSelected);
        }
        AssertSelectedFeatures(expected, viewModel.SelectedFeatures);

        viewModel.AddFireplaceCommand.Execute(null);
        var saved = Assert.Single(viewModel.Fireplaces);
        Assert.Equal(size, saved.Size);
        AssertSelectedFeatures(expected, saved.Features);
        Assert.Empty(viewModel.SelectedFeatures);

        viewModel.EditFireplaceCommand.Execute(saved);
        Assert.True(viewModel.IsEditingFireplace);
        Assert.Equal(modelName, viewModel.Model);
        Assert.Equal(size, viewModel.Size);
        AssertSelectedFeatures(expected, viewModel.SelectedFeatures);
        foreach (var key in expected.Keys)
            Assert.True(Assert.Single(viewModel.AllFeatureOptions, item => item.Key == key).IsSelected);

        viewModel.AddFireplaceCommand.Execute(null);
        var updated = Assert.Single(viewModel.Fireplaces);
        Assert.NotSame(saved, updated);
        Assert.False(viewModel.IsEditingFireplace);
        AssertSelectedFeatures(expected, updated.Features);
    }

    private static void AssertSelectedFeatures(IReadOnlyDictionary<string, string> expected,
                                               IEnumerable<FeatureSelection> features)
    {
        var actual = features.ToArray();
        Assert.Equal(expected.Keys.OrderBy(key => key), actual.Select(item => item.Key).OrderBy(key => key));
        foreach (var (key, label) in expected)
            Assert.Equal(label, Assert.Single(actual, item => item.Key == key).DisplayName);
    }

    private static MainViewModel CreateViewModel()
    {
        var logger = new NullLogger();
        return new MainViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService(),
            new MediaSelectionService(), new EmptyPrices(), new EmptyPdf(), new MemorySettings(),
            new DraftWorkflowService(new EmptyGmail(), new EmailTemplateService(), logger), logger);
    }

    private sealed class EmptyPrices : IPriceBookService
    {
        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookWorkbook());
        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());
        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
                                                        CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());
        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
                                                           CancellationToken cancellationToken = default) =>
            Task.FromResult(new PricedQuoteResult { Request = request, Success = true });
        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(QuoteRequest request, string pricingPath,
                                                                            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceLinkSet>>([]);
    }

    private sealed class EmptyPdf : IQuotePdfService
    {
        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
                                               CancellationToken cancellationToken = default) =>
            Task.FromResult(outputPath);
    }

    private sealed class MemorySettings : ISettingsService
    {
        private AppSettings _settings = new() { PricingFile = "test-pricing.xlsx" };
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyGmail : IGmailDraftService
    {
        public Task<string> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSenderDisplayAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> GetSignatureHtmlAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task DeleteDraftAsync(string draftId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmailDraftResult> CreateDraftAsync(EmailDraftRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailDraftResult());
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogFilePath => string.Empty;
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
