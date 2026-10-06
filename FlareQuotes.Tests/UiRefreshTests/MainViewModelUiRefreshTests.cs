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

    [Fact]
    public void FireplaceQuantitySurvivesAddEditAndSave()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.DecreaseFireplaceQuantityCommand.CanExecute(null));
        Assert.True(viewModel.IncreaseFireplaceQuantityCommand.CanExecute(null));

        viewModel.IncreaseFireplaceQuantityCommand.Execute(null);
        Assert.Equal(2, viewModel.FireplaceQuantity);
        viewModel.DecreaseFireplaceQuantityCommand.Execute(null);
        Assert.Equal(1, viewModel.FireplaceQuantity);

        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceQuantity = 3;

        viewModel.AddFireplaceCommand.Execute(null);

        var original = Assert.Single(viewModel.Fireplaces);
        Assert.Equal(3, original.Quantity);
        Assert.Equal("3", viewModel.FireplaceCountText);

        viewModel.EditFireplaceCommand.Execute(original);
        Assert.Equal(3, viewModel.FireplaceQuantity);

        viewModel.FireplaceQuantity = 2;
        viewModel.AddFireplaceCommand.Execute(null);

        var updated = Assert.Single(viewModel.Fireplaces);
        Assert.Equal(2, updated.Quantity);
        Assert.Equal("2", viewModel.FireplaceCountText);
        Assert.Equal(1, viewModel.FireplaceQuantity);
    }

    [Fact]
    public void MultipleAdditionalClassicMediaSurviveAddEditAndSave()
    {
        var viewModel = CreateViewModel(mediaService: new MediaSelectionService());
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";

        var selectedOptions = viewModel.AdditionalClassicMediaOptions.Take(2).ToList();
        Assert.Equal(2, selectedOptions.Count);
        foreach (var option in selectedOptions)
            viewModel.SelectAdditionalClassicMediaCommand.Execute(option);

        var expectedKeys = selectedOptions.Select(option => option.Key).OrderBy(key => key).ToList();
        Assert.Equal(2, viewModel.SelectedAdditionalClassicMedia.Count);

        viewModel.AddFireplaceCommand.Execute(null);

        var original = Assert.Single(viewModel.Fireplaces);
        Assert.Equal(expectedKeys, SplitKeys(original.AdditionalClassicMediaKey));
        Assert.Equal(2, original.PremiumMedia.Count(media =>
                         media.Key.StartsWith("additional_classic::", StringComparison.OrdinalIgnoreCase)));

        viewModel.EditFireplaceCommand.Execute(original);

        Assert.Equal(expectedKeys,
                     viewModel.SelectedAdditionalClassicMedia.Select(media => media.Key).OrderBy(key => key).ToList());

        viewModel.AddFireplaceCommand.Execute(null);

        var updated = Assert.Single(viewModel.Fireplaces);
        Assert.Equal(expectedKeys, SplitKeys(updated.AdditionalClassicMediaKey));
        Assert.Equal(2, updated.PremiumMedia.Count(media =>
                         media.Key.StartsWith("additional_classic::", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MultipleAdditionalClassicMediaSurviveQuoteRecallAndEdit()
    {
        var viewModel = CreateViewModel(mediaService: new MediaSelectionService());
        var snapshot = new MainViewModel.LastQuoteSnapshot
        {
            ProjectName = "Recalled project",
            Fireplaces =
            [
                new FireplaceQuoteDraft
                {
                    FireplaceLabel = "Recalled fireplace",
                    Model = "Front Facing",
                    Size = "60",
                    GlassHeight = "16",
                    AdditionalClassicMediaKey = "fg_silver|fg_bronze",
                    PremiumMedia =
                    [
                        new MediaSelection
                        {
                            Key = "additional_classic::fg_silver",
                            DisplayName = "Reflective Silver Fire Glass",
                            IsPremium = false
                        },
                        new MediaSelection
                        {
                            Key = "additional_classic::fg_bronze",
                            DisplayName = "Reflective Bronze Fire Glass",
                            IsPremium = false
                        }
                    ]
                }
            ]
        };

        viewModel.RecallQuoteCommand.Execute(snapshot);
        var recalled = Assert.Single(viewModel.Fireplaces);
        viewModel.EditFireplaceCommand.Execute(recalled);

        Assert.Equal(
            ["fg_bronze", "fg_silver"],
            viewModel.SelectedAdditionalClassicMedia.Select(media => media.Key).OrderBy(key => key).ToList());
    }

    [Fact]
    public async Task IncompletePricingBlocksPdfPreviewGeneration()
    {
        var pdf = new TrackingPdfService();
        var viewModel = CreateViewModel(
            priceBookService: new IncompletePriceBookService(),
            quotePdfService: pdf);
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";

        await viewModel.NextToPreviewCommand.ExecuteAsync(null);

        Assert.Equal(0, pdf.BuildCallCount);
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.GeneratedPdfPath));
        Assert.Contains("Missing price", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PdfPreviewUsesBrandingSavedAfterViewModelWasCreated()
    {
        var settings = new MemorySettingsService();
        var pdf = new TrackingPdfService();
        var viewModel = CreateViewModel(quotePdfService: pdf, settingsService: settings);
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";

        await settings.SaveAsync(
            new AppSettings
            {
                PricingFile = "test-pricing.xlsx",
                SalesEmail = "updated@example.com",
                SalesPhone = "312-555-0199",
                Website = "https://flarefireplaces.com/updated"
            });

        await viewModel.NextToPreviewCommand.ExecuteAsync(null);

        Assert.NotNull(pdf.LastRequest);
        Assert.Equal("updated@example.com", pdf.LastRequest.Branding.SalesEmail);
        Assert.Equal("312-555-0199", pdf.LastRequest.Branding.SalesPhone);
        Assert.Equal("https://flarefireplaces.com/updated", pdf.LastRequest.Branding.Website);

        viewModel.ApplySettings(
            new AppSettings
            {
                PricingFile = "test-pricing.xlsx",
                SalesEmail = "newer@example.com",
                SalesPhone = "312-555-0100",
                Website = "https://flarefireplaces.com/newer"
            });

        Assert.True(string.IsNullOrWhiteSpace(viewModel.GeneratedPdfPath));
        Assert.Equal(QuoteWorkflowStage.Review, viewModel.WorkflowStage);
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

    [Theory]
    [InlineData("TRA-BON-42", "42")]
    [InlineData("TRABON42", "42")]
    [InlineData("BONTR42", "42")]
    [InlineData("TRA-BON-46", "46")]
    [InlineData("TRABON46", "46")]
    [InlineData("BONTRA46", "46")]
    [InlineData("Flare-TRA-BON-46", "46")]
    public void BonfireModelCodeKeepsItsIdentityThroughAddAndEdit(string modelCode, string size)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser(),
            new FeatureSelectionService(), mediaService: new MediaSelectionService());

        viewModel.Model = modelCode;

        Assert.Equal("Traditional Bonfire", viewModel.Model);
        Assert.Equal(size, viewModel.Size);
        Assert.Empty(viewModel.GlassHeight);
        viewModel.AllPremiumMediaOptions.Single(option => option.Key == "gold_glass").IsSelected = true;
        viewModel.AddFireplaceCommand.Execute(null);

        var added = Assert.Single(viewModel.Fireplaces);
        Assert.Equal($"TRA-BON-{size}", added.Model);
        Assert.Equal("gold_glass", Assert.Single(added.PremiumMedia).Key);
        Assert.Contains("Bonfire", added.FireplaceLabel, StringComparison.Ordinal);

        viewModel.EditFireplaceCommand.Execute(added);

        Assert.Equal("Traditional Bonfire", viewModel.Model);
        Assert.Equal(size, viewModel.Size);
        Assert.Equal("gold_glass", Assert.Single(viewModel.SelectedPremiumMedia).Key);
        viewModel.AddFireplaceCommand.Execute(null);

        Assert.Equal($"TRA-BON-{size}", Assert.Single(viewModel.Fireplaces).Model);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("46")]
    public void BonfireAndRegularTraditionalExposeTheSameOptionalFeatures(string size)
    {
        var traditional = CreateViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService());
        traditional.Model = $"TR-{size}";
        var bonfire = CreateViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService());
        bonfire.Model = $"TRA-BON-{size}";

        var expected = traditional.AllFeatureOptions.Select(option => option.Key).OrderBy(key => key).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, bonfire.AllFeatureOptions.Select(option => option.Key).OrderBy(key => key).ToArray());
        Assert.Contains(expected, key => key == "offset_red_brick_traditional");
        Assert.Contains(expected, key => key == "herringbone_black_brick_traditional");
    }

    [Theory]
    [InlineData("42", "loak42", "loak46")]
    [InlineData("46", "loak46", "loak42")]
    public void BonfireOffersGeneralPremiumMediaAndOnlyItsMatchingOakLogs(string size, string oakKey, string otherOakKey)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser(), mediaService: new MediaSelectionService());
        viewModel.Model = $"TRA-BON-{size}";

        var keys = viewModel.AllPremiumMediaOptions.Select(option => option.Key).ToArray();
        foreach (var generalKey in new[] { "gold_glass", "aqua_glass", "chestnut_glass", "black_stones", "white_stones", "grey_balls_2",
                                          "grey_balls_4", "grey_balls_mixed", "white_balls_2", "white_balls_4",
                                          "white_balls_mixed", "black_balls_2", "black_balls_4", "black_balls_mixed",
                                          "driftwood", "birchwood" })
            Assert.Contains(generalKey, keys);
        Assert.Contains(oakKey, keys);
        Assert.DoesNotContain(otherOakKey, keys);
        Assert.DoesNotContain("tr42bch", keys);
        Assert.DoesNotContain("tr46bch", keys);
    }

    [Theory]
    [InlineData("42", "tr42bch")]
    [InlineData("46", "tr46bch")]
    public void RegularTraditionalKeepsItsExistingPremiumChoices(string size, string birchLogsKey)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser(), mediaService: new MediaSelectionService());
        viewModel.Model = $"DVTRA{size}";

        Assert.Equal(new[] { "birchwood", birchLogsKey },
            viewModel.AllPremiumMediaOptions.Select(option => option.Key).OrderBy(key => key).ToArray());
    }

    [Fact]
    public void ChangingBonfireSizeDropsOnlyTheIncompatibleOakSelection()
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser(), mediaService: new MediaSelectionService());
        viewModel.Model = "TRA-BON-42";
        viewModel.AllPremiumMediaOptions.Single(option => option.Key == "gold_glass").IsSelected = true;
        viewModel.AllPremiumMediaOptions.Single(option => option.Key == "loak42").IsSelected = true;

        viewModel.Model = "TRA-BON-46";

        Assert.Equal("46", viewModel.Size);
        Assert.Equal("gold_glass", Assert.Single(viewModel.SelectedPremiumMedia).Key);
        Assert.Contains(viewModel.AllPremiumMediaOptions, option => option.Key == "loak46");
        Assert.DoesNotContain(viewModel.AllPremiumMediaOptions, option => option.Key == "loak42");
    }

    [Theory]
    [InlineData("DVFF60H", "DVFF80R", "80", "16")]
    [InlineData("DVFF60H", "DVFF60EH", "60", "30")]
    [InlineData("DVTRA42", "DVTRA46", "46", "")]
    public void ChangingFullCodeUpdatesDimensionsWhenTheDisplayModelStaysTheSame(
        string firstCode, string secondCode, string size, string glassHeight)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser());
        viewModel.Model = firstCode;
        var displayModel = viewModel.Model;

        viewModel.Model = secondCode;

        Assert.Equal(displayModel, viewModel.Model);
        Assert.Equal(size, viewModel.Size);
        Assert.Equal(glassHeight, viewModel.GlassHeight);
    }

    [Theory]
    [InlineData("TRA-BON-42", "42")]
    [InlineData("TRABON46", "46")]
    [InlineData("Flare-TRA-BON-46", "46")]
    [InlineData("DVTRA42", "42")]
    [InlineData("TR-46", "46")]
    public void SwitchingToAFullTraditionalCodeClearsThePreviousGlassHeight(string traditionalCode, string size)
    {
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser());
        viewModel.Model = "DVFF60H";
        Assert.Equal("24", viewModel.GlassHeight);

        viewModel.Model = traditionalCode;

        Assert.Equal(size, viewModel.Size);
        Assert.Empty(viewModel.GlassHeight);
        Assert.DoesNotContain("glass", viewModel.CurrentFireplaceLabel, StringComparison.OrdinalIgnoreCase);
        viewModel.AddFireplaceCommand.Execute(null);
        var fireplace = Assert.Single(viewModel.Fireplaces);
        Assert.Empty(fireplace.GlassHeight);

        viewModel.EditFireplaceCommand.Execute(fireplace);

        Assert.Equal(size, viewModel.Size);
        Assert.Empty(viewModel.GlassHeight);
    }

    [Theory]
    [InlineData("TRA-BON-42", "42")]
    [InlineData("TRABON46", "46")]
    [InlineData("BONTR42", "42")]
    public void BonfireSpecCardsKeepModelAndHeading(string modelCode, string size)
    {
        var viewModel = CreateViewModel();
        viewModel.SpecLinks.Add(new SpecLinkDraft
        {
            FireplaceCode = modelCode,
            FireplaceLocation = "Great Room",
            Label = "Product Sheet",
            Url = "https://flarefireplaces.com/traditional-product.pdf"
        });

        var card = Assert.Single(viewModel.UrlVerificationFireplaces);
        Assert.Equal($"TRA-BON-{size}", card.ModelCode);
        Assert.Equal("TR", card.StyleKey);
        Assert.Equal("Traditional Bonfire", card.StyleLabel);
        Assert.Equal($"Great Room — Traditional Bonfire {size} URLs", card.UrlHeading);
        Assert.EndsWith("/TR.png", card.ImagePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TR42", "42")]
    [InlineData("TRA46", "46")]
    public void RegularTraditionalSpecHeadingsRemainDistinct(string modelCode, string size)
    {
        var viewModel = CreateViewModel();
        viewModel.SpecLinks.Add(new SpecLinkDraft
        {
            FireplaceCode = modelCode,
            Label = "Product Sheet",
            Url = "https://flarefireplaces.com/traditional-product.pdf"
        });

        var card = Assert.Single(viewModel.UrlVerificationFireplaces);
        Assert.Equal(modelCode, card.ModelCode);
        Assert.Equal("Traditional", card.StyleLabel);
        Assert.Equal($"Traditional {size} URLs", card.UrlHeading);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("46")]
    public async Task BonfirePreviewUsesCanonicalQuoteIdentity(string size)
    {
        var pdf = new TrackingPdfService();
        var viewModel = CreateViewModel(new DefaultQuoteRequestParser(), new FeatureSelectionService(),
            new FixedEstimatePriceBookService(4200m), new MediaSelectionService(), pdf);
        viewModel.Model = $"TRA-BON-{size}";
        viewModel.AllPremiumMediaOptions.Single(option => option.Key == "gold_glass").IsSelected = true;

        await viewModel.NextToPreviewCommand.ExecuteAsync(null);

        Assert.NotNull(pdf.LastRequest);
        var fireplace = Assert.Single(pdf.LastRequest.Fireplaces);
        Assert.Equal(FireplaceType.Traditional, fireplace.Type);
        Assert.Equal($"TRA-BON-{size}", fireplace.Model);
        Assert.Equal(size, fireplace.Size);
        Assert.Equal("gold_glass", Assert.Single(fireplace.PremiumMedia).Key);
        Assert.Equal($"TRA-BON-{size}", Assert.Single(viewModel.QuotePreviewRows).FireplaceLabel);
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
    public void KeyboardMoveCommandsRespectBoundariesAndReorderFireplaceCards()
    {
        var viewModel = CreateViewModel();
        var first = new FireplaceQuoteDraft { FireplaceLabel = "First", Model = "FF50" };
        var second = new FireplaceQuoteDraft { FireplaceLabel = "Second", Model = "ST60" };
        var third = new FireplaceQuoteDraft { FireplaceLabel = "Third", Model = "VDC70" };

        viewModel.Fireplaces.Add(first);
        viewModel.Fireplaces.Add(second);
        viewModel.Fireplaces.Add(third);

        Assert.False(viewModel.MoveFireplaceUpCommand.CanExecute(first));
        Assert.True(viewModel.MoveFireplaceDownCommand.CanExecute(first));
        Assert.True(viewModel.MoveFireplaceUpCommand.CanExecute(second));
        Assert.True(viewModel.MoveFireplaceDownCommand.CanExecute(second));
        Assert.True(viewModel.MoveFireplaceUpCommand.CanExecute(third));
        Assert.False(viewModel.MoveFireplaceDownCommand.CanExecute(third));

        viewModel.MoveFireplaceDownCommand.Execute(first);

        Assert.Equal([second, first, third], viewModel.Fireplaces);
        Assert.Contains("position 2", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.MoveFireplaceUpCommand.CanExecute(first));

        viewModel.MoveFireplaceUpCommand.Execute(first);

        Assert.Equal([first, second, third], viewModel.Fireplaces);
        Assert.Contains("position 1", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.MoveFireplaceUpCommand.CanExecute(first));
    }

    [Fact]
    public void FireplaceCardUsesLocationAsItsNameWithoutRepeatingItInDetails()
    {
        var located = new FireplaceQuoteDraft
        {
            FireplaceLabel = "Living Room | FF50 | 50\" | 16\" glass",
            Location = " Living Room ",
            Model = "FF50",
            Size = "50",
            GlassHeight = "16"
        };
        var labeled = new FireplaceQuoteDraft { FireplaceLabel = "Existing label", Model = "FF60" };
        var modelOnly = new FireplaceQuoteDraft { Model = "ST70" };

        Assert.Equal("Living Room", located.DisplayName);
        Assert.Equal("FF50  ·  50  ·  16 glass", located.DetailLine);
        Assert.DoesNotContain("Living Room", located.DetailLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Existing label", labeled.DisplayName);
        Assert.Equal("ST70", modelOnly.DisplayName);
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
    public void UrlVerificationDistinguishesDuplicateModelsByFireplaceLocation()
    {
        var viewModel = CreateViewModel();

        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "001:FF60R",
                FireplaceCode = "FF60R",
                FireplaceLocation = "Living Room",
                Label = "Product Sheet",
                Url = "https://example.com/ff60-product.pdf",
                Status = "specific"
            });
        viewModel.SpecLinks.Add(
            new SpecLinkDraft
            {
                FireplaceGroupId = "002:FF60R",
                FireplaceCode = "FF60R",
                FireplaceLocation = "Primary Bedroom",
                Label = "Product Sheet",
                Url = "https://example.com/ff60-product.pdf",
                Status = "specific"
            });

        Assert.Collection(
            viewModel.UrlVerificationFireplaces,
            card =>
            {
                Assert.Equal("Living Room", card.FireplaceLocation);
                Assert.StartsWith("Living Room — ", card.UrlHeading, StringComparison.Ordinal);
            },
            card =>
            {
                Assert.Equal("Primary Bedroom", card.FireplaceLocation);
                Assert.StartsWith("Primary Bedroom — ", card.UrlHeading, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task QuotePreviewRowsPreserveEachFireplaceLocation()
    {
        var viewModel = CreateViewModel(priceBookService: new FixedEstimatePriceBookService(4200m));

        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = "Living Room";
        viewModel.AddFireplaceCommand.Execute(null);

        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = "Primary Bedroom";
        viewModel.AddFireplaceCommand.Execute(null);

        var priced = new PricedQuoteResult
        {
            Fireplaces =
            [
                new PricedFireplaceQuote { ModelNumber = "FF60R", FireplaceLocation = "Living Room" },
                new PricedFireplaceQuote { ModelNumber = "FF60R", FireplaceLocation = "Primary Bedroom" }
            ]
        };
        var buildPreviewRows = typeof(MainViewModel).GetMethod(
            "BuildQuotePreviewRows",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(buildPreviewRows);
        buildPreviewRows!.Invoke(viewModel, [priced]);

        Assert.Collection(
            viewModel.QuotePreviewRows,
            row => Assert.Equal("Living Room", row.FireplaceLocation),
            row => Assert.Equal("Primary Bedroom", row.FireplaceLocation));

        await viewModel.NextToSpecLinksCommand.ExecuteAsync(null);

        Assert.True(viewModel.SpecLinks.Count == 2, viewModel.StatusMessage);
        Assert.Collection(
            viewModel.UrlVerificationFireplaces,
            card => Assert.Equal("Living Room", card.FireplaceLocation),
            card => Assert.Equal("Primary Bedroom", card.FireplaceLocation));
    }

    [Fact]
    public void FrontFacingDoesNotExposeOutdoorKitOptionalFeature()
    {
        var viewModel = CreateViewModel(featureService: new FeatureSelectionService());

        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "24";

        Assert.DoesNotContain(
            viewModel.AllFeatureOptions,
            option => option.DisplayName.Contains("Outdoor Kit", StringComparison.OrdinalIgnoreCase) ||
                      option.Key.Contains("outdoor_kit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FeatureChipRemoveCommandClearsSelectedFeature()
    {
        var viewModel = CreateViewModel(featureService: new FeatureSelectionService());

        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "24";

        var option = Assert.Single(
            viewModel.AllFeatureOptions,
            feature => string.Equals(feature.Key, "rgb_leds", StringComparison.OrdinalIgnoreCase));

        viewModel.ToggleFeatureCommand.Execute(option);
        var selected = Assert.Single(
            viewModel.SelectedFeatures,
            feature => string.Equals(feature.Key, "rgb_leds", StringComparison.OrdinalIgnoreCase));

        viewModel.RemoveFeatureCommand.Execute(selected);

        Assert.DoesNotContain(
            viewModel.SelectedFeatures,
            feature => string.Equals(feature.Key, "rgb_leds", StringComparison.OrdinalIgnoreCase));
        Assert.False(option.IsSelected);
    }

    [Fact]
    public async Task EditingQuoteDoesNotPriceUntilPreviewIsRequested()
    {
        var prices = new FixedEstimatePriceBookService(4321m);
        var viewModel = CreateViewModel(priceBookService: prices);
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "24";
        await Task.Delay(250);
        Assert.Equal(0, prices.BuildCallCount);
        await viewModel.NextToPreviewCommand.ExecuteAsync(null);
        Assert.Equal(1, prices.BuildCallCount);
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

    [Fact]
    public void DuplicateFireplaceDeepCopiesConfigurationAndOpensItForEditing()
    {
        var viewModel = CreateViewModel(featureService: new FeatureSelectionService());
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "24";
        viewModel.FireplaceLocation = "Living Room";
        viewModel.FireplaceQuantity = 3;
        viewModel.ToggleFeatureCommand.Execute(viewModel.AllFeatureOptions.First());
        viewModel.AddFireplaceCommand.Execute(null);
        var original = Assert.Single(viewModel.Fireplaces);
        viewModel.DuplicateFireplaceCommand.Execute(original);
        Assert.Equal(2, viewModel.Fireplaces.Count);
        var duplicate = viewModel.Fireplaces[1];
        Assert.Equal(3, duplicate.Quantity);
        Assert.Equal("Living Room (copy)", duplicate.Location);
        Assert.Equal(original.Features.Select(feature => feature.Key), duplicate.Features.Select(feature => feature.Key));
        Assert.NotSame(original.Features, duplicate.Features);
        Assert.NotSame(original.Features[0], duplicate.Features[0]);
        Assert.True(viewModel.IsEditingFireplace);
        Assert.Equal("Save Changes", viewModel.AddFireplaceButtonText);
        viewModel.FireplaceLocation = "Bedroom";
        viewModel.AddFireplaceCommand.Execute(null);
        Assert.Equal("Living Room", original.Location);
        Assert.Equal("Bedroom", viewModel.Fireplaces[1].Location);
        Assert.Equal(2, viewModel.Fireplaces.Count);
    }

    [Fact]
    public void DuplicateDoesNotDiscardAnUnsavedFireplace()
    {
        var viewModel = CreateViewModel();
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.AddFireplaceCommand.Execute(null);
        var original = Assert.Single(viewModel.Fireplaces);
        viewModel.Model = "See Through";
        Assert.False(viewModel.DuplicateFireplaceCommand.CanExecute(original));
        viewModel.DuplicateFireplaceCommand.Execute(original);
        Assert.Single(viewModel.Fireplaces);
        Assert.Equal("See Through", viewModel.Model);
    }

    [Fact]
    public void RemovingOnePhotoWithADuplicateFilenameKeepsTheOtherAndDoesNotDeleteFiles()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"first-{Guid.NewGuid():N}");
        var secondPath = Path.Combine(Path.GetTempPath(), $"second-{Guid.NewGuid():N}");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        var first = Path.Combine(firstPath, "fireplace.png");
        var second = Path.Combine(secondPath, "fireplace.png");
        File.WriteAllBytes(first, new byte[1024]);
        File.WriteAllBytes(second, new byte[2048]);
        try
        {
            var viewModel = CreateViewModel();
            viewModel.FireplacePhotoPaths.Add(first);
            viewModel.FireplacePhotoPaths.Add(second);
            Assert.Equal(2, viewModel.FireplacePhotoItems.Count);
            viewModel.RemoveFireplacePhotoCommand.Execute(viewModel.FireplacePhotoItems[0]);
            Assert.Equal(second, Assert.Single(viewModel.FireplacePhotoPaths));
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Equal("2 KB", Assert.Single(viewModel.FireplacePhotoItems).SizeText);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            Directory.Delete(firstPath);
            Directory.Delete(secondPath);
        }
    }

    [Fact]
    public void DuplicateManualLinkLabelsAreRejectedWithinTheSameFireplace()
    {
        var viewModel = CreateViewModel();
        viewModel.SpecLinks.Add(new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Product Sheet", Url = "https://flarefireplaces.com/original.pdf" });
        viewModel.ManualUrlToolName = " product sheet ";
        viewModel.ManualUrlValue = "https://flarefireplaces.com/new.pdf";
        viewModel.AddManualUrlCommand.Execute(null);
        Assert.Single(viewModel.SpecLinks);
        Assert.Contains("already has a link", viewModel.SpecLinkValidationMessage);
        Assert.Equal("https://flarefireplaces.com/original.pdf", viewModel.SpecLinks[0].Url);
        Assert.Equal(" product sheet ", viewModel.ManualUrlToolName);
        viewModel.SpecLinks.Add(new SpecLinkDraft { FireplaceGroupId = "two", FireplaceCode = "FF60R", Label = "Other Guide", Url = "https://flarefireplaces.com/other.pdf" });
        viewModel.SelectUrlVerificationFireplaceCommand.Execute(viewModel.UrlVerificationFireplaces[1]);
        viewModel.AddManualUrlCommand.Execute(null);
        Assert.Equal(3, viewModel.SpecLinks.Count);
        Assert.Empty(viewModel.SpecLinkValidationMessage);
    }

    [Fact]
    public void LinkEditingBlocksDraftAndValidatesWithoutOverwritingExistingLinks()
    {
        var viewModel = CreateViewModel();
        viewModel.Email = "customer@example.com";
        var first = new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Product Sheet", Url = "https://flarefireplaces.com/original.pdf" };
        var second = new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Guide", Url = "https://flarefireplaces.com/guide.pdf" };
        viewModel.SpecLinks.Add(first);
        viewModel.SpecLinks.Add(second);
        var row = viewModel.SelectedUrlVerificationRows[0];
        viewModel.EditSpecLinkCommand.Execute(row);
        Assert.False(viewModel.CanCreateGmailDraft);
        row.EditingLabel = "guide";
        row.EditingUrl = "https://flarefireplaces.com/changed.pdf";
        viewModel.SaveSpecLinkCommand.Execute(row);
        Assert.True(row.IsEditing);
        Assert.Contains("already has a link", row.ValidationMessage);
        Assert.Equal("Product Sheet", first.Label);
        row.EditingLabel = "Updated Product";
        row.EditingUrl = "https://unapproved.example/changed.pdf";
        viewModel.SaveSpecLinkCommand.Execute(row);
        Assert.True(row.IsEditing);
        Assert.Equal("https://flarefireplaces.com/original.pdf", first.Url);
        row.EditingUrl = "https://flarefireplaces.com/changed.pdf";
        viewModel.SaveSpecLinkCommand.Execute(row);
        Assert.Equal("Updated Product", first.Label);
        Assert.Equal("https://flarefireplaces.com/changed.pdf", first.Url);
        Assert.Equal("Guide", second.Label);
        Assert.True(viewModel.CanCreateGmailDraft);
        Assert.False(viewModel.HasUnsavedSpecLinkEdits);
    }

    [Fact]
    public void CancelLinkEditLeavesOriginalLabelAndUrlIntact()
    {
        var viewModel = CreateViewModel();
        var link = new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Guide", Url = "https://flarefireplaces.com/guide.pdf" };
        viewModel.SpecLinks.Add(link);
        var row = Assert.Single(viewModel.SelectedUrlVerificationRows);
        viewModel.EditSpecLinkCommand.Execute(row);
        row.EditingLabel = "Unsaved";
        row.EditingUrl = "https://flarefireplaces.com/unsaved.pdf";
        viewModel.CancelSpecLinkEditCommand.Execute(row);
        Assert.Equal("Guide", link.Label);
        Assert.Equal("https://flarefireplaces.com/guide.pdf", link.Url);
        Assert.False(row.IsEditing);
    }

    [Fact]
    public void PendingLinkEditSurvivesAttemptsToSwitchCardsEditAnotherRowAddOrDeleteUrls()
    {
        var viewModel = CreateViewModel();
        viewModel.Email = "customer@example.com";
        var editedLink = new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Guide", Url = "https://flarefireplaces.com/guide.pdf" };
        viewModel.SpecLinks.Add(editedLink);
        viewModel.SpecLinks.Add(new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Product", Url = "https://flarefireplaces.com/product.pdf" });
        viewModel.SpecLinks.Add(new SpecLinkDraft { FireplaceGroupId = "two", FireplaceCode = "ST60R", Label = "Other Guide", Url = "https://flarefireplaces.com/other.pdf" });
        var selectedCard = viewModel.SelectedUrlVerificationFireplace;
        var otherCard = viewModel.UrlVerificationFireplaces[1];
        var row = viewModel.SelectedUrlVerificationRows[0];
        var otherRow = viewModel.SelectedUrlVerificationRows[1];
        viewModel.EditSpecLinkCommand.Execute(row);
        row.EditingLabel = "Still typing a name";
        row.EditingUrl = "https://flarefireplaces.com/still-typing.pdf";
        viewModel.ManualUrlToolName = "Extra";
        viewModel.ManualUrlValue = "https://flarefireplaces.com/extra.pdf";

        Assert.False(viewModel.SelectUrlVerificationFireplaceCommand.CanExecute(otherCard));
        Assert.False(viewModel.EditSpecLinkCommand.CanExecute(otherRow));
        Assert.False(viewModel.AddManualUrlCommand.CanExecute(null));
        Assert.False(viewModel.RemoveSpecLinkCommand.CanExecute(otherRow));
        Assert.True(viewModel.SaveSpecLinkCommand.CanExecute(row));
        Assert.True(viewModel.CancelSpecLinkEditCommand.CanExecute(row));
        Assert.False(viewModel.SaveSpecLinkCommand.CanExecute(otherRow));

        // Guard direct execution too, so queued actions cannot replace the active editor.
        viewModel.SelectUrlVerificationFireplaceCommand.Execute(otherCard);
        viewModel.EditSpecLinkCommand.Execute(otherRow);
        viewModel.AddManualUrlCommand.Execute(null);
        viewModel.RemoveSpecLinkCommand.Execute(otherRow);

        Assert.Same(selectedCard, viewModel.SelectedUrlVerificationFireplace);
        Assert.Same(row, viewModel.SelectedUrlVerificationRows[0]);
        Assert.True(row.IsEditing);
        Assert.False(otherRow.IsEditing);
        Assert.Equal("Still typing a name", row.EditingLabel);
        Assert.Equal("https://flarefireplaces.com/still-typing.pdf", row.EditingUrl);
        Assert.Equal("Guide", editedLink.Label);
        Assert.Equal(3, viewModel.SpecLinks.Count);
        Assert.Equal("Extra", viewModel.ManualUrlToolName);
        Assert.False(viewModel.CanCreateGmailDraft);
        Assert.Contains("Save or cancel", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Save or cancel", viewModel.GmailDraftRequirementText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavingOrCancelingLinkEditReenablesReviewActionsAndRefreshesDraftRequirements(bool save)
    {
        var viewModel = CreateViewModel();
        viewModel.Email = "customer@example.com";
        var link = new SpecLinkDraft { FireplaceGroupId = "one", FireplaceCode = "FF60R", Label = "Guide", Url = "https://flarefireplaces.com/guide.pdf" };
        viewModel.SpecLinks.Add(link);
        viewModel.SpecLinks.Add(new SpecLinkDraft { FireplaceGroupId = "two", FireplaceCode = "ST60R", Label = "Product", Url = "https://flarefireplaces.com/product.pdf" });
        var row = Assert.Single(viewModel.SelectedUrlVerificationRows);
        viewModel.EditSpecLinkCommand.Execute(row);
        row.EditingLabel = "Changed Guide";
        row.EditingUrl = "https://flarefireplaces.com/changed.pdf";
        var addCommandNotifications = 0;
        var draftCommandNotifications = 0;
        viewModel.AddManualUrlCommand.CanExecuteChanged += (_, _) => addCommandNotifications++;
        viewModel.CreateDraftCommand.CanExecuteChanged += (_, _) => draftCommandNotifications++;
        HashSet<string> changedProperties = [];
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };

        if (save)
            viewModel.SaveSpecLinkCommand.Execute(row);
        else
            viewModel.CancelSpecLinkEditCommand.Execute(row);

        Assert.Equal(save ? "Changed Guide" : "Guide", link.Label);
        Assert.Equal(save ? "https://flarefireplaces.com/changed.pdf" : "https://flarefireplaces.com/guide.pdf", link.Url);
        Assert.False(viewModel.HasUnsavedSpecLinkEdits);
        Assert.True(viewModel.AddManualUrlCommand.CanExecute(null));
        Assert.True(viewModel.RemoveSpecLinkCommand.CanExecute(Assert.Single(viewModel.SelectedUrlVerificationRows)));
        Assert.True(viewModel.SelectUrlVerificationFireplaceCommand.CanExecute(viewModel.UrlVerificationFireplaces[1]));
        Assert.True(viewModel.CreateDraftCommand.CanExecute(null));
        Assert.Contains("customer@example.com", viewModel.GmailDraftRequirementText, StringComparison.Ordinal);
        Assert.Empty(viewModel.SpecLinkValidationMessage);
        Assert.True(addCommandNotifications > 0);
        Assert.True(draftCommandNotifications > 0);
        Assert.Contains(nameof(MainViewModel.GmailDraftRequirementText), changedProperties);
        Assert.Contains(nameof(MainViewModel.CanCreateGmailDraft), changedProperties);

        viewModel.SelectUrlVerificationFireplaceCommand.Execute(viewModel.UrlVerificationFireplaces[1]);
        Assert.Equal("two", viewModel.SelectedUrlVerificationFireplace?.GroupId);
        Assert.True(viewModel.EditSpecLinkCommand.CanExecute(Assert.Single(viewModel.SelectedUrlVerificationRows)));
    }

    [Fact]
    public void FreshMessageContextDoesNotUseLoadedQuoteHistory()
    {
        var viewModel = CreateViewModel();
        SetCompletedMessageFixture(viewModel, useImmediately: false);

        var context = viewModel.GetTextMessageContext();

        Assert.Empty(context.ClientName);
        Assert.Empty(context.Phone);
        Assert.Equal("Current quote", context.SourceLabel);
    }

    [Fact]
    public void TypedMessageContextTakesPrecedenceOverCompletedQuote()
    {
        var viewModel = CreateViewModel();
        SetCompletedMessageFixture(viewModel, useImmediately: true);
        viewModel.ClientName = "New Client";
        viewModel.Phone = "3125550200";
        viewModel.ProjectName = "New project";

        var context = viewModel.GetTextMessageContext();

        Assert.Equal("New Client", context.ClientName);
        Assert.Equal("3125550200", context.Phone);
        Assert.Equal("New project", context.ProjectName);
        Assert.Equal("Current quote", context.SourceLabel);
    }

    [Fact]
    public void ImmediatePostDraftMessageContextUsesCompletedQuote()
    {
        var viewModel = CreateViewModel();
        SetCompletedMessageFixture(viewModel, useImmediately: true);

        var context = viewModel.GetTextMessageContext();

        Assert.Equal("Amanda Jensen", context.ClientName);
        Assert.Equal("3125550184", context.Phone);
        Assert.Equal("Lakeview Renovation", context.ProjectName);
        Assert.Equal("Front Facing 60\"", context.Model);
        Assert.Equal("Last quote", context.SourceLabel);
    }

    [Fact]
    public void ExplicitClearRemovesCompletedMessageContextAndKeepsRecallHistory()
    {
        var viewModel = CreateViewModel();
        var snapshot = SetCompletedMessageFixture(viewModel, useImmediately: true);
        Assert.Equal("Amanda Jensen", viewModel.GetTextMessageContext().ClientName);

        viewModel.ClearCommand.Execute(null);

        Assert.Empty(viewModel.GetTextMessageContext().ClientName);
        Assert.Empty(viewModel.GetTextMessageContext().Phone);
        Assert.Contains(snapshot, viewModel.RecentQuoteHistory);
    }

    [Theory]
    [InlineData(nameof(MainViewModel.Postal))]
    [InlineData(nameof(MainViewModel.InstallDate))]
    [InlineData(nameof(MainViewModel.Size))]
    [InlineData(nameof(MainViewModel.GlassHeight))]
    [InlineData(nameof(MainViewModel.FireplaceLocation))]
    public void StartingAnotherManualQuoteDoesNotReusePreviousRecipients(string property)
    {
        var viewModel = CreateViewModel();
        SetCompletedMessageFixture(viewModel, useImmediately: true);
        typeof(MainViewModel).GetProperty(property)!.SetValue(viewModel, "60");

        var context = viewModel.GetTextMessageContext();

        Assert.Empty(context.ClientName);
        Assert.Empty(context.Phone);
        Assert.Equal("Current quote", context.SourceLabel);
    }

    private static MainViewModel.LastQuoteSnapshot SetCompletedMessageFixture(MainViewModel viewModel, bool useImmediately)
    {
        var snapshot = new MainViewModel.LastQuoteSnapshot
        {
            ClientName = "Amanda Jensen",
            Phone = "3125550184",
            ProjectName = "Lakeview Renovation",
            Fireplaces = [new FireplaceQuoteDraft { Model = "Front Facing", Size = "60" }]
        };
        viewModel.RecentQuoteHistory.Add(snapshot);
        // Configure history state without persisting fixture customers into the Windows profile.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(MainViewModel).GetField("_lastCompletedQuoteSnapshot", flags)!.SetValue(viewModel, snapshot);
        typeof(MainViewModel).GetField("_useCompletedQuoteForMessages", flags)!.SetValue(viewModel, useImmediately);
        return snapshot;
    }

    private static MainViewModel CreateViewModel(
        IQuoteRequestParser? parser = null,
        IFeatureSelectionService? featureService = null,
        IPriceBookService? priceBookService = null,
        IMediaSelectionService? mediaService = null,
        IQuotePdfService? quotePdfService = null,
        ISettingsService? settingsService = null)
    {
        var logger = new NullLogger();
        var draftWorkflow = new DraftWorkflowService(new NullGmailDraftService(), new EmailTemplateService(), logger);

        return new MainViewModel(parser ?? new EmptyParser(), featureService ?? new EmptyFeatureService(),
                                 mediaService ?? new EmptyMediaService(),
                                 priceBookService ?? new EmptyPriceBookService(),
                                 quotePdfService ?? new EmptyPdfService(), settingsService ?? new MemorySettingsService(),
                                 draftWorkflow,
                                 logger);
    }

    private static List<string> SplitKeys(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(key => key)
            .ToList();

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

    private sealed class FixedEstimatePriceBookService : IPriceBookService
    {
        private readonly decimal _price;
        public int BuildCallCount { get; private set; }

        public FixedEstimatePriceBookService(decimal price)
        {
            _price = price;
        }

        public Task<PriceBookWorkbook> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookWorkbook { SourcePath = path });

        public Task<PriceBookMatch> FindBaseModelAsync(QuoteRequest request,
                                                       CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());

        public Task<PriceBookMatch> FindFeaturePriceAsync(QuoteRequest request, FeatureOption feature,
                                                          CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBookMatch());

        public Task<PricedQuoteResult> BuildPricedQuoteAsync(QuoteRequest request, string pricingPath,
                                                             CancellationToken cancellationToken = default)
        {
            BuildCallCount++;
            var result = new PricedQuoteResult { Request = request, Success = true };

            var count = request.Fireplaces.Count;
            if (count == 0 && !string.IsNullOrWhiteSpace(request.Model))
                count = 1;

            for (var i = 0; i < count; i++)
            {
                var fireplace = i < request.Fireplaces.Count ? request.Fireplaces[i] : null;
                result.Fireplaces.Add(
                    new PricedFireplaceQuote
                    {
                        FireplaceLabel = fireplace?.Model ?? request.Model,
                        FireplaceLocation = fireplace?.FireplaceLocation ?? request.FireplaceLocation,
                        Model = fireplace?.Model ?? request.Model,
                        ModelNumber = fireplace?.Model ?? request.Model,
                        LeadTime = fireplace?.LeadTime ?? "3-5 Business Days",
                        BaseLine = new PriceLine
                        {
                            Feature = "Fireplace",
                            Price = _price
                        }
                    });
            }

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(
            QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ResourceLinkSet> links = request.Fireplaces.Select(
                fireplace =>
                {
                    var set = new ResourceLinkSet
                    {
                        ModelNumber = fireplace.Model,
                        FireplaceLocation = fireplace.FireplaceLocation
                    };
                    set.Links["Product Sheet"] = "https://example.com/product.pdf";
                    set.Sources["Product Sheet"] = "specific";
                    return set;
                }).ToList();

            return Task.FromResult(links);
        }
    }

    private sealed class IncompletePriceBookService : IPriceBookService
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
            Task.FromResult(
                new PricedQuoteResult
                {
                    Request = request,
                    Success = false,
                    Message = "Missing price for selected option."
                });

        public Task<IReadOnlyList<ResourceLinkSet>> ResolveResourceLinksAsync(
            QuoteRequest request, string pricingPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceLinkSet>>([]);
    }

    private sealed class TrackingPdfService : IQuotePdfService
    {
        public int BuildCallCount { get; private set; }
        public QuoteRequest? LastRequest { get; private set; }

        public Task<string> BuildQuotePdfAsync(QuoteRequest request, string outputPath,
                                               CancellationToken cancellationToken = default)
        {
            BuildCallCount++;
            LastRequest = request;
            return Task.FromResult(outputPath);
        }
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
