#if FLARE_UI_SNAPSHOTS
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Messaging;
using FlareQuotes.Core.Models;

namespace FlareQuotes.App.Views;

internal static class UiSnapshotCapture
{
    private const string SnapshotModeVariable = "FLARE_UI_SNAPSHOT_MODE";
    private const string SnapshotDirectoryVariable = "FLARE_UI_SNAPSHOT_DIR";
    private const string SnapshotTimeoutVariable = "FLARE_UI_SNAPSHOT_TIMEOUT_SECONDS";
    private const int DefaultSnapshotTimeoutSeconds = 90;

    public static async Task<bool> TryCaptureAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(SnapshotModeVariable), "1",
                           StringComparison.Ordinal))
        {
            return false;
        }

        var requestedDirectory = Environment.GetEnvironmentVariable(SnapshotDirectoryVariable);
        if (string.IsNullOrWhiteSpace(requestedDirectory))
        {
            Application.Current.Shutdown(1);
            return true;
        }

        string? snapshotDirectory = null;
        CancellationTokenSource? timeoutCancellation = null;
        Task? timeoutGuard = null;
        var exitCode = 0;
        try
        {
            snapshotDirectory = Path.GetFullPath(requestedDirectory);
            timeoutCancellation = new CancellationTokenSource();
            timeoutGuard = RunTimeoutGuardAsync(snapshotDirectory, timeoutCancellation.Token);
            Directory.CreateDirectory(snapshotDirectory);

            var renderWindow = new MainWindow();
            if (renderWindow.DataContext is not MainViewModel viewModel)
                throw new InvalidOperationException("The main window view model was not available.");

            PopulateRepresentativeQuote(viewModel);

            // Use an unattached render surface. This avoids live TextBox bindings writing the
            // initially empty on-screen values back over the deterministic representative data.
            var mainFrame = renderWindow.WindowFrame;
            renderWindow.Content = null;
            mainFrame.DataContext = viewModel;
            var nativeMainMetrics = WindowAppearance.VerifyNativeAttributes(renderWindow);

            ArrangeAtSize(mainFrame, 1480, 920);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-dark.png"));
            var mainMetrics = ValidateMainWindow(renderWindow, mainFrame, viewModel);

            ArrangeAtSize(mainFrame, 1180, 760);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-minimum.png"));
            var minimumMainMetrics = ValidateMinimumMainWindow(renderWindow, mainFrame);

            // The spec-row actions use a Window ancestor for their command bindings. Reattach
            // the populated surface after the original main captures without showing a window.
            renderWindow.Content = mainFrame;
            PopulateRepresentativeSpecLinks(viewModel);

            ArrangeAtSize(mainFrame, 1480, 920);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "spec-links-dark.png"));
            var specLinksMetrics = ValidateSpecLinks(mainFrame, viewModel);

            ArrangeAtSize(mainFrame, 1180, 760);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "spec-links-minimum.png"));
            var minimumSpecLinksMetrics = ValidateSpecLinks(mainFrame, viewModel);

            var editingRow = viewModel.SelectedUrlVerificationRows[0];
            viewModel.EditSpecLinkCommand.Execute(editingRow);
            editingRow.EditingLabel = "Product information";
            renderWindow.SpecLinksStageScroller.ScrollToHome();
            ArrangeAtSize(mainFrame, 1180, 760);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "spec-links-editing-minimum.png"));
            var specEditingMetrics = ValidateSpecLinkEditor(mainFrame, viewModel, editingRow);
            viewModel.CancelSpecLinkEditCommand.Execute(editingRow);

            PopulateRepresentativePhotos(viewModel, snapshotDirectory);
            ArrangeAtSize(mainFrame, 1180, 760);
            renderWindow.SpecLinksStageScroller.ScrollToEnd();
            mainFrame.UpdateLayout();
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "spec-links-photos-minimum.png"));
            var photoMetrics = ValidatePhotoAttachments(renderWindow, mainFrame, viewModel);
            viewModel.FireplacePhotoPaths.Clear();
            renderWindow.SpecLinksStageScroller.ScrollToHome();

            var textMessageWindow = new TextMessageWindow(new TextMessageContext
            {
                ClientName = "Amanda Jensen",
                ProjectName = "Lakeview Renovation",
                Model = "FF60R",
                Phone = "(312) 555-0184",
                SalesName = "Kyle",
                ConsultationUrl = TextMessageTemplateRenderer.DefaultConsultationUrl
            }, new TextMessageTemplateStore(Path.Combine(snapshotDirectory, "snapshot-text-templates.json.dpapi")));
            var nativeTextMessageMetrics = WindowAppearance.VerifyNativeAttributes(textMessageWindow);
            var textMessageFrame = textMessageWindow.TextMessageFrame;
            textMessageWindow.Content = null;

            ArrangeAtSize(textMessageFrame, 920, 720);
            SaveVisual(textMessageFrame, Path.Combine(snapshotDirectory, "text-message-composer.png"));
            var textMessageMetrics = ValidateTextMessage(textMessageWindow, textMessageFrame, editing: false);
            ArrangeAtSize(textMessageFrame, 800, 650);
            SaveVisual(textMessageFrame, Path.Combine(snapshotDirectory, "text-message-composer-minimum.png"));
            var minimumTextMessageMetrics = ValidateTextMessage(textMessageWindow, textMessageFrame, editing: false);

            textMessageWindow.EditTemplateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            textMessageWindow.TemplateBodyBox.Text += "\n— {SalesName}";
            ArrangeAtSize(textMessageFrame, 920, 720);
            SaveVisual(textMessageFrame, Path.Combine(snapshotDirectory, "text-message-editor.png"));
            var textMessageEditorMetrics = ValidateTextMessage(textMessageWindow, textMessageFrame, editing: true);
            ArrangeAtSize(textMessageFrame, 800, 650);
            SaveVisual(textMessageFrame, Path.Combine(snapshotDirectory, "text-message-editor-minimum.png"));
            var minimumTextMessageEditorMetrics = ValidateTextMessage(textMessageWindow, textMessageFrame, editing: true);

            var settingsWindow = new SettingsWindow {
                Width = 920,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            PopulateRepresentativeSettings(settingsWindow);

            var settingsFrame = settingsWindow.SettingsFrame;
            settingsWindow.Content = null;

            ArrangeAtSize(settingsFrame, 920, 700);
            SaveVisual(settingsFrame, Path.Combine(snapshotDirectory, "settings-window-dark.png"));
            var settingsMetrics = ValidateSettingsWindow(settingsWindow, settingsFrame);

            ArrangeAtSize(settingsFrame, 820, 620);
            SaveVisual(settingsFrame, Path.Combine(snapshotDirectory, "settings-window-minimum.png"));
            var minimumSettingsMetrics = ValidateSettingsWindow(settingsWindow, settingsFrame);

            ApplySnapshotTheme(renderWindow, dark: false);
            viewModel.WorkflowStage = QuoteWorkflowStage.Review;
            ArrangeAtSize(mainFrame, 1480, 920);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-light.png"));
            var lightMainMetrics = ValidateMainWindow(renderWindow, mainFrame, viewModel, expectedDarkTheme: false);
            var cancelTemplateButton = FindVisualElements<Button>(textMessageWindow.EditorPanel)
                                           .Single(button => AutomationProperties.GetName(button) == "Cancel text message template editing");
            cancelTemplateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            textMessageWindow.Content = textMessageFrame;
            WindowAppearance.UpdateTheme(textMessageWindow, dark: false);
            ArrangeAtSize(textMessageFrame, 920, 720);
            SaveVisual(textMessageFrame, Path.Combine(snapshotDirectory, "text-message-light.png"));
            var lightTextMessageMetrics = ValidateTextMessage(textMessageWindow, textMessageFrame, editing: false);
            ApplySnapshotTheme(renderWindow, dark: true);
            WindowAppearance.UpdateTheme(textMessageWindow, dark: true);

            PopulateRepresentativeBonfire(viewModel);
            ArrangeAtSize(mainFrame, 1480, 920);
            ScrollBonfireSelectionsIntoView(renderWindow, mainFrame);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "bonfire-selection-dark.png"));
            var bonfireMetrics = ValidateBonfireSelection(renderWindow, mainFrame, viewModel);
            ArrangeAtSize(mainFrame, 1180, 760);
            ScrollBonfireSelectionsIntoView(renderWindow, mainFrame);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "bonfire-selection-minimum.png"));
            var minimumBonfireMetrics = ValidateBonfireSelection(renderWindow, mainFrame, viewModel);

            var metrics = new {
                generatedUtc = DateTime.UtcNow,
                mainWindow = mainMetrics,
                minimumMainWindow = minimumMainMetrics,
                specLinks = specLinksMetrics,
                minimumSpecLinks = minimumSpecLinksMetrics,
                specLinkEditor = specEditingMetrics,
                photoAttachments = photoMetrics,
                textMessage = textMessageMetrics,
                minimumTextMessage = minimumTextMessageMetrics,
                textMessageEditor = textMessageEditorMetrics,
                minimumTextMessageEditor = minimumTextMessageEditorMetrics,
                nativeMainWindow = nativeMainMetrics,
                nativeTextMessageWindow = nativeTextMessageMetrics,
                lightMainWindow = lightMainMetrics,
                lightTextMessage = lightTextMessageMetrics,
                bonfireSelection = bonfireMetrics,
                minimumBonfireSelection = minimumBonfireMetrics,
                settingsWindow = settingsMetrics,
                minimumSettingsWindow = minimumSettingsMetrics,
                systemHealthWindowsOpened = Application.Current.Windows.OfType<SystemHealthWindow>().Count()
            };

            File.WriteAllText(Path.Combine(snapshotDirectory, "layout-metrics.json"),
                              JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            exitCode = 1;
            TryWriteSnapshotError(snapshotDirectory ?? requestedDirectory, ex.ToString());
        }
        finally
        {
            if (timeoutCancellation is not null)
            {
                timeoutCancellation.Cancel();
                if (timeoutGuard is not null)
                {
                    try
                    {
                        await timeoutGuard;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when capture completes before the fail-safe deadline.
                    }
                }

                timeoutCancellation.Dispose();
            }

            Application.Current.Shutdown(exitCode);
        }

        return true;
    }

    private static async Task RunTimeoutGuardAsync(string snapshotDirectory, CancellationToken cancellationToken)
    {
        var seconds = DefaultSnapshotTimeoutSeconds;
        if (int.TryParse(Environment.GetEnvironmentVariable(SnapshotTimeoutVariable), out var configuredSeconds))
            seconds = Math.Clamp(configuredSeconds, 10, 300);

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        TryWriteSnapshotError(
            snapshotDirectory,
            $"UI snapshot capture exceeded its {seconds}-second fail-safe and was terminated.");
        Environment.Exit(124);
    }

    private static void TryWriteSnapshotError(string directory, string message)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullDirectory);
            File.WriteAllText(Path.Combine(fullDirectory, "snapshot-error.txt"), message);
        }
        catch
        {
            // The outer runner also enforces a timeout and captures a runner-level error.
        }
    }

    private static void PopulateRepresentativeQuote(MainViewModel viewModel)
    {
        viewModel.RawRequest =
            "Hi Flare team,\n\nPlease prepare a quote for the Jensen residence.\n\n" +
            "Project: Lakeview Renovation\nModel: Front Facing 60\"\nLocation: Living Room\n" +
            "Install target: September 2026\n\nCustomer: Amanda Jensen\n" +
            "amanda@example.com\n(312) 555-0184\n\n" +
            "Please include black reflective glass and standard lead time.";
        viewModel.ProjectName = "Lakeview Renovation";
        viewModel.ClientName = "Amanda Jensen";
        viewModel.Email = "amanda@example.com";
        viewModel.Phone = "(312) 555-0184";
        viewModel.Postal = "Chicago, IL 60614";
        viewModel.InstallDate = "September 2026";
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = "Living Room";
        viewModel.FireplaceQuantity = 3;
        viewModel.StatusMessage = "Customer and fireplace details detected.";

        var passiveHeatFlex = viewModel.FilteredFeatureOptions.FirstOrDefault(option =>
            string.Equals(option.Key, "passive_heat_flex", StringComparison.OrdinalIgnoreCase));
        if (passiveHeatFlex is null)
            throw new InvalidOperationException("Passive Heat Flex was not available for the representative fireplace.");

        viewModel.ToggleFeatureCommand.Execute(passiveHeatFlex);

        var classicMedia = viewModel.FilteredClassicMediaOptions.FirstOrDefault();
        if (classicMedia is not null)
            viewModel.SelectClassicMediaCommand.Execute(classicMedia);

        viewModel.AddFireplaceCommand.Execute(null);
        var savedFireplace = viewModel.Fireplaces.SingleOrDefault();
        if (savedFireplace is null || savedFireplace.Quantity != 3 ||
            savedFireplace.Features.All(feature =>
                !string.Equals(feature.Key, "passive_heat_flex", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The representative quantity and Passive Heat Flex selection were not preserved on the quote.");
        }

        // Keep the saved summary card visible while restoring the same fireplace in the builder,
        // so one snapshot exercises both quantity presentations and the Passive Heat Flex chip.
        viewModel.EditFireplaceCommand.Execute(savedFireplace);
    }

    private static void PopulateRepresentativeSettings(SettingsWindow window)
    {
        window.SalesEmailBox.Text = "quotes@example.com";
        window.SalesPhoneBox.Text = "(512) 555-0187";
        window.WebsiteBox.Text = "https://flarefireplaces.com";
        window.ConsultationUrlBox.Text = "https://meetings.hubspot.com/flare/consultation";
        window.RecallQuoteHistoryLimitBox.Text = "5";
        window.SettingsStatusText.Text = "Settings are stored securely under your Windows profile.";
    }

    private static void PopulateRepresentativeBonfire(MainViewModel viewModel)
    {
        viewModel.ClearCommand.Execute(null);
        viewModel.ProjectName = "Lakeview Renovation";
        viewModel.ClientName = "Amanda Jensen";
        viewModel.Email = "amanda@example.com";
        viewModel.RawRequest = "Model: TRA-BON-42\nLocation: Great Room\n" +
                               "Reflective Black Interior, Gold Glass, Driftwood, Large Oak Logs";
        viewModel.Model = "TRA-BON-42";
        viewModel.FireplaceLocation = "Great Room";
        viewModel.AllFeatureOptions.Single(option => option.Key == "reflective_black_interior").IsSelected = true;
        foreach (var key in new[] { "gold_glass", "driftwood", "loak42" })
            viewModel.AllPremiumMediaOptions.Single(option => option.Key == key).IsSelected = true;
        viewModel.AddFireplaceCommand.Execute(null);
        viewModel.EditFireplaceCommand.Execute(viewModel.Fireplaces.Single());
        viewModel.WorkflowStage = QuoteWorkflowStage.Review;
        viewModel.StatusMessage = "Traditional Bonfire selections ready to review.";
    }

    private static object ValidateBonfireSelection(MainWindow window, FrameworkElement root, MainViewModel viewModel)
    {
        var builderScroller = BonfireBuilderScroller(window);
        var builderViewport = FindVisualElements<ScrollContentPresenter>(builderScroller).First();
        var saved = viewModel.Fireplaces.Single();
        if (viewModel.Model != "Traditional Bonfire" || viewModel.Size != "42" || saved.Model != "TRA-BON-42" ||
            viewModel.SelectedFeatures.Single().Key != "reflective_black_interior" ||
            viewModel.SelectedPremiumMedia.Count != 3 || saved.PremiumMedia.Count != 3)
            throw new InvalidOperationException("The Bonfire model and selected features/media did not survive editing.");

        var premiumKeys = viewModel.AllPremiumMediaOptions.Select(option => option.Key).ToArray();
        foreach (var key in new[] { "gold_glass", "aqua_glass", "black_stones", "driftwood", "birchwood", "loak42" })
        {
            if (!premiumKeys.Contains(key))
                throw new InvalidOperationException($"Bonfire premium media '{key}' was not available.");
        }
        if (premiumKeys.Any(key => key is "tr42bch" or "tr46bch" or "loak46"))
            throw new InvalidOperationException("Bonfire offered media reserved for another Traditional model.");
        foreach (var key in new[] { "double_glass", "summer_kit", "power_vent", "herringbone_black_brick_traditional" })
        {
            if (!viewModel.AllFeatureOptions.Any(option => option.Key == key))
                throw new InvalidOperationException($"Traditional feature '{key}' was not available for Bonfire.");
        }

        var modelBox = FindVisualElements<TextBox>(root)
                           .Single(box => AutomationProperties.GetName(box) == "Fireplace model");
        AssertWithinRoot(modelBox, root, "Traditional Bonfire model input");
        AssertWithinViewport(modelBox, builderViewport, root, "Traditional Bonfire model input within builder");
        AssertBindingHasNoError(modelBox, TextBox.TextProperty, "Traditional Bonfire model binding");
        if (modelBox.Text != "Traditional Bonfire")
            throw new InvalidOperationException("The model field did not display Traditional Bonfire.");
        AssertWithinRoot(window.FeatureDropdownButton, root, "Bonfire feature selector");
        AssertWithinRoot(window.PremiumMediaDropdownButton, root, "Bonfire premium media selector");
        AssertWithinViewport(window.FeatureDropdownButton, builderViewport, root, "Bonfire feature selector within builder");
        AssertWithinViewport(window.PremiumMediaDropdownButton, builderViewport, root, "Bonfire premium selector within builder");
        var headings = FindVisualElements<TextBlock>(window.QuoteWorkspacePane)
                           .Where(text => BindingOperations.GetBindingExpression(text, TextBlock.TextProperty)
                                              ?.ParentBinding.Path?.Path == "CurrentFireplaceHeading").ToArray();
        if (headings.Length == 0 || headings.Any(heading => heading.Text != "Traditional Bonfire"))
            throw new InvalidOperationException("The current fireplace heading did not visibly identify Traditional Bonfire.");
        foreach (var heading in headings)
        {
            AssertWithinViewport(heading, builderViewport, root, "Traditional Bonfire heading within builder");
            AssertBindingHasNoError(heading, TextBlock.TextProperty, "Traditional Bonfire current heading binding");
        }
        foreach (var selection in viewModel.SelectedFeatures)
        {
            var chip = FindVisualElements<TextBlock>(window.QuoteWorkspacePane)
                           .Single(text => ReferenceEquals(text.DataContext, selection) && text.Text == selection.DisplayName);
            AssertWithinViewport(SelectionChipBorder(chip), builderViewport, root, $"Bonfire {selection.DisplayName} feature chip within builder");
            AssertBindingHasNoError(chip, TextBlock.TextProperty, $"Bonfire {selection.DisplayName} feature chip binding");
        }
        foreach (var selection in viewModel.SelectedPremiumMedia)
        {
            var chip = FindVisualElements<TextBlock>(window.QuoteWorkspacePane)
                           .Single(text => ReferenceEquals(text.DataContext, selection) && text.Text == selection.DisplayName);
            AssertWithinRoot(chip, root, $"Bonfire {selection.DisplayName} chip");
            AssertWithinViewport(SelectionChipBorder(chip), builderViewport, root, $"Bonfire {selection.DisplayName} chip within builder");
            AssertBindingHasNoError(chip, TextBlock.TextProperty, $"Bonfire {selection.DisplayName} chip binding");
        }
        return new
        {
            width = root.ActualWidth,
            height = root.ActualHeight,
            model = saved.Model,
            traditionalFeaturesVerified = true,
            premiumOptionsVerified = true,
            selectedPremiumMediaCount = viewModel.SelectedPremiumMedia.Count,
            selectionsInsideViewport = true,
            bonfireHeadingVisible = true,
            builderVerticalOffset = builderScroller.VerticalOffset,
            bindingErrorCount = 0
        };
    }

    private static void ScrollBonfireSelectionsIntoView(MainWindow window, FrameworkElement root)
    {
        BonfireBuilderScroller(window).ScrollToEnd();
        root.UpdateLayout();
    }

    private static ScrollViewer BonfireBuilderScroller(MainWindow window)
    {
        for (var parent = VisualTreeHelper.GetParent(window.PremiumMediaDropdownButton); parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer scroller)
                return scroller;
        }
        throw new InvalidOperationException("The Bonfire selection builder scrollbar could not be located.");
    }

    private static Border SelectionChipBorder(FrameworkElement chipText)
    {
        for (var parent = VisualTreeHelper.GetParent(chipText); parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is Border border)
                return border;
        }
        throw new InvalidOperationException("The selection chip container could not be located.");
    }

    private static void PopulateRepresentativeSpecLinks(MainViewModel viewModel)
    {
        viewModel.SpecLinks.Clear();
        viewModel.SpecLinks.Add(new SpecLinkDraft
        {
            FireplaceGroupId = "snapshot:living-room",
            FireplaceCode = "FF60R",
            FireplaceLocation = "Living Room",
            Label = "Product Sheet",
            Url = "https://flarefireplaces.com/wp-content/uploads/product-sheets/Front-Facing-60.pdf",
            Status = "matched"
        });
        viewModel.SpecLinks.Add(new SpecLinkDraft
        {
            FireplaceGroupId = "snapshot:living-room",
            FireplaceCode = "FF60R",
            FireplaceLocation = "Living Room",
            Label = "Installation Manual",
            Url = "https://flarefireplaces.com/wp-content/uploads/installation/Flare-Fireplaces-Installation-Manual.pdf",
            Status = "matched"
        });

        viewModel.ManualUrlToolName = "Framing Supplement";
        viewModel.ManualUrlValue =
            "https://flarefireplaces.com/wp-content/uploads/installation/supplemental-framing-notes.pdf";
        viewModel.AddManualUrlCommand.Execute(null);

        viewModel.SpecLinks.Add(new SpecLinkDraft
        {
            FireplaceGroupId = "snapshot:patio",
            FireplaceCode = "VST50H",
            FireplaceLocation = "Covered Patio",
            Label = "Product Sheet",
            Url = "https://flarefireplaces.com/wp-content/uploads/product-sheets/Outdoor-See-Through-50.pdf",
            Status = "matched"
        });
        viewModel.WorkflowStage = QuoteWorkflowStage.SpecLinks;
        viewModel.StatusMessage = "Review the selected fireplace's automatic and manually added URLs.";

        if (viewModel.UrlVerificationFireplaces.Count != 2 || viewModel.SelectedUrlVerificationRows.Count != 3 ||
            viewModel.SelectedUrlVerificationRows.Count(row => row.SourceLink?.Status == "manual") != 1)
        {
            throw new InvalidOperationException("Representative automatic and manual spec URLs were not preserved.");
        }
    }

    private static void ArrangeAtSize(FrameworkElement root, double width, double height)
    {
        root.Width = width;
        root.Height = height;
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static object ValidateMainWindow(MainWindow window, FrameworkElement root, MainViewModel viewModel,
                                             bool expectedDarkTheme = true)
    {
        AssertWithinRoot(window.RequestPane, root, nameof(window.RequestPane));
        AssertWithinRoot(window.QuoteWorkspacePane, root, nameof(window.QuoteWorkspacePane));
        AssertWithinRoot(window.FireplaceSummaryPane, root, nameof(window.FireplaceSummaryPane));
        AssertWithinRoot(window.GeneratePreviewButton, root, nameof(window.GeneratePreviewButton));
        AssertWithinRoot(window.ThemeToggleButton, root, nameof(window.ThemeToggleButton));
        AssertWithinRoot(window.DecreaseFireplaceQuantityButton, root,
                         nameof(window.DecreaseFireplaceQuantityButton));
        AssertWithinRoot(window.FireplaceQuantityValue, root, nameof(window.FireplaceQuantityValue));
        AssertWithinRoot(window.IncreaseFireplaceQuantityButton, root,
                         nameof(window.IncreaseFireplaceQuantityButton));

        AssertRange(window.RequestPane.ActualWidth, 355, 365, "Request pane width");
        AssertRange(window.QuoteWorkspacePane.ActualWidth, 690, 750, "Quote workspace width");
        AssertRange(window.FireplaceSummaryPane.ActualWidth, 335, 345, "Fireplace summary width");
        AssertRange(window.GeneratePreviewButton.ActualWidth, 295, 315, "Generate preview width");
        AssertRange(window.GeneratePreviewButton.ActualHeight, 34, 42, "Generate preview height");
        AssertRange(window.ThemeToggleButton.ActualWidth, 36, 40, "Theme button width");

        if (window.ThemeToggleButton.IsChecked != expectedDarkTheme)
            throw new InvalidOperationException("The deterministic snapshot did not use the requested theme.");

        if (viewModel.FireplaceQuantity != 3 || window.FireplaceQuantityValue.Text != "3")
            throw new InvalidOperationException("The representative fireplace quantity did not render as 3.");

        var passiveHeatFlexChipCount = CountVisualText(root, "Passive Heat Flex");
        var savedQuantityLabelCount = CountVisualText(root, "Qty 3");
        if (passiveHeatFlexChipCount < 2)
            throw new InvalidOperationException("Passive Heat Flex did not render in the builder and saved card.");
        if (savedQuantityLabelCount < 1)
            throw new InvalidOperationException("The saved fireplace card did not render its Qty 3 label.");

        AssertAutomationName(window.ThemeToggleButton, "Switch light or dark theme");
        AssertAutomationName(window.RecallLastQuoteButton, "Open recent quotes");
        AssertAutomationName(window.AddFireplaceButton, "Add or save current fireplace");
        AssertAutomationName(window.DecreaseFireplaceQuantityButton, "Decrease fireplace quantity");
        AssertAutomationName(window.FireplaceQuantityValue, "Fireplace quantity");
        AssertAutomationName(window.IncreaseFireplaceQuantityButton, "Increase fireplace quantity");
        AssertAutomationName(window.FeatureDropdownButton, "Select additional features");
        AssertAutomationName(window.GeneratePreviewButton, "Generate quote preview");

        if (Application.Current.Windows.OfType<SystemHealthWindow>().Any())
            throw new InvalidOperationException("The system health window opened during normal startup.");

        return new {
            width = root.ActualWidth,
            height = root.ActualHeight,
            requestPaneWidth = window.RequestPane.ActualWidth,
            workspaceWidth = window.QuoteWorkspacePane.ActualWidth,
            fireplaceSummaryWidth = window.FireplaceSummaryPane.ActualWidth,
            generateButtonWidth = window.GeneratePreviewButton.ActualWidth,
            generateButtonHeight = window.GeneratePreviewButton.ActualHeight,
            themeButtonWidth = window.ThemeToggleButton.ActualWidth,
            representativeQuantity = viewModel.FireplaceQuantity,
            passiveHeatFlexSelected = viewModel.SelectedFeatures.Any(feature =>
                string.Equals(feature.Key, "passive_heat_flex", StringComparison.OrdinalIgnoreCase)),
            passiveHeatFlexChipCount,
            savedQuantityLabelCount
        };
    }

    private static object ValidateSettingsWindow(SettingsWindow window, FrameworkElement root)
    {
        AssertWithinRoot(window.SettingsCancelButton, root, nameof(window.SettingsCancelButton));
        AssertWithinRoot(window.SettingsSaveButton, root, nameof(window.SettingsSaveButton));
        AssertRange(window.SettingsCancelButton.ActualHeight, 32, 36, "Settings cancel height");
        AssertRange(window.SettingsSaveButton.ActualHeight, 32, 36, "Settings save height");
        AssertRange(window.SettingsSaveButton.ActualWidth, 120, 150, "Settings save width");
        AssertAutomationName(window.SettingsCancelButton, "Cancel settings changes");
        AssertAutomationName(window.SettingsSaveButton, "Save settings");
        AssertAutomationName(window.RunSystemHealthButton, "Run security and system health check");

        return new {
            width = root.ActualWidth,
            height = root.ActualHeight,
            cancelButtonWidth = window.SettingsCancelButton.ActualWidth,
            cancelButtonHeight = window.SettingsCancelButton.ActualHeight,
            saveButtonWidth = window.SettingsSaveButton.ActualWidth,
            saveButtonHeight = window.SettingsSaveButton.ActualHeight
        };
    }

    private static object ValidateMinimumMainWindow(MainWindow window, FrameworkElement root)
    {
        AssertWithinRoot(window.RequestPane, root, nameof(window.RequestPane));
        AssertWithinRoot(window.QuoteWorkspacePane, root, nameof(window.QuoteWorkspacePane));
        AssertWithinRoot(window.FireplaceSummaryPane, root, nameof(window.FireplaceSummaryPane));
        AssertWithinRoot(window.GeneratePreviewButton, root, nameof(window.GeneratePreviewButton));
        AssertRange(window.RequestPane.ActualWidth, 355, 365, "Minimum request pane width");
        AssertRange(window.QuoteWorkspacePane.ActualWidth, 390, 450, "Minimum quote workspace width");
        AssertRange(window.FireplaceSummaryPane.ActualWidth, 335, 345, "Minimum fireplace summary width");
        AssertRange(window.GeneratePreviewButton.ActualHeight, 34, 42, "Minimum generate preview height");

        return new {
            width = root.ActualWidth,
            height = root.ActualHeight,
            requestPaneWidth = window.RequestPane.ActualWidth,
            workspaceWidth = window.QuoteWorkspacePane.ActualWidth,
            fireplaceSummaryWidth = window.FireplaceSummaryPane.ActualWidth,
            generateButtonWidth = window.GeneratePreviewButton.ActualWidth,
            generateButtonHeight = window.GeneratePreviewButton.ActualHeight
        };
    }

    private static object ValidateSpecLinks(FrameworkElement root, MainViewModel viewModel)
    {
        var buttons = FindVisualElements<Button>(root)
                          .Where(button => ReferenceEquals(button.Command, viewModel.RemoveSpecLinkCommand))
                          .ToArray();
        var urlBoxes = FindVisualElements<TextBox>(root)
                           .Where(textBox => AutomationProperties.GetName(textBox) == "Resource URL")
                           .ToArray();
        var rows = viewModel.SelectedUrlVerificationRows;

        if (buttons.Length != rows.Count || urlBoxes.Length != rows.Count)
            throw new InvalidOperationException("Every selected spec URL must render a URL field and Delete URL action.");

        foreach (var row in rows)
        {
            var button = buttons.SingleOrDefault(candidate => ReferenceEquals(candidate.DataContext, row));
            var urlBox = urlBoxes.SingleOrDefault(candidate => ReferenceEquals(candidate.DataContext, row));
            if (button is null || urlBox is null ||
                !ReferenceEquals(button.Command, viewModel.RemoveSpecLinkCommand) ||
                !ReferenceEquals(button.CommandParameter, row) || button.Command?.CanExecute(row) != true)
            {
                throw new InvalidOperationException($"Delete URL is not bound to the selected '{row.Item}' row.");
            }

            AssertWithinRoot(button, root, $"{row.Item} Delete URL button");
            AssertWithinRoot(urlBox, root, $"{row.Item} URL field");
            AssertAutomationName(button, $"Delete {row.Item}");
            AssertBindingHasNoError(button, Button.CommandProperty, $"{row.Item} delete command");
            AssertBindingHasNoError(button, Button.CommandParameterProperty, $"{row.Item} delete parameter");
            AssertBindingHasNoError(urlBox, TextBox.TextProperty, $"{row.Item} URL text");
            if (!string.Equals(urlBox.Text, row.Url, StringComparison.Ordinal))
                throw new InvalidOperationException($"The URL field did not render '{row.Item}' correctly.");
        }

        foreach (var element in FindVisualElements<FrameworkElement>(root))
        {
            var automationName = AutomationProperties.GetName(element);
            if (automationName is "Custom resource name" or "Custom resource URL")
                AssertWithinRoot(element, root, automationName);
        }

        return new
        {
            width = root.ActualWidth,
            height = root.ActualHeight,
            fireplaceCardCount = viewModel.UrlVerificationFireplaces.Count,
            selectedRowCount = rows.Count,
            manualRowCount = rows.Count(row => row.SourceLink?.Status == "manual"),
            deleteButtonCount = buttons.Length,
            resourceUrlFieldCount = urlBoxes.Length,
            rowBindingErrorCount = 0
        };
    }

    private static object ValidateTextMessage(TextMessageWindow window, FrameworkElement root, bool editing)
    {
        AssertWithinRoot(window.TemplateList, root, "Text template selector");
        AssertWithinRoot(window.SalesNameBox, root, "Message sender name");
        AssertWithinRoot(window.AddTemplateButton, root, "Add message template");
        AssertWithinRoot(window.EditTemplateButton, root, "Edit message template");
        AssertWithinRoot(window.DeleteTemplateButton, root, "Delete message template");
        AssertAutomationName(window.TemplateList, "Text message templates");
        AssertAutomationName(window.SalesNameBox, "Your name for message templates");
        if (window.TemplateList.Items.Count != 3)
            throw new InvalidOperationException("The message dialog did not render three seeded templates.");

        var boundTemplateTexts = FindVisualElements<TextBlock>(window.TemplateList)
                                     .Where(text => text.DataContext is TextMessageTemplate &&
                                                    BindingOperations.GetBindingExpression(text, TextBlock.TextProperty) is not null)
                                     .ToArray();
        if (boundTemplateTexts.Length != 6)
            throw new InvalidOperationException("The template selector did not render every name and message preview.");
        foreach (var text in boundTemplateTexts)
            AssertBindingHasNoError(text, TextBlock.TextProperty, "Template selector text");

        if (editing)
        {
            AssertWithinRoot(window.TemplateNameBox, root, "Template name editor");
            AssertWithinRoot(window.TemplateBodyBox, root, "Template message editor");
            AssertWithinRoot(window.EditorPreviewText, root, "Live template preview");
            AssertWithinRoot(window.EditorStatusText, root, "Template token feedback");
            foreach (var button in FindVisualElements<Button>(window.EditorPanel))
                AssertWithinRoot(button, root, $"Template editor {button.Content} button");
            if (CountVisualText(root, "Template name") != 1 ||
                !window.EditorPreviewText.Text.Contains("Hi Amanda,", StringComparison.Ordinal) ||
                !window.EditorPreviewText.Text.Contains("Lakeview Renovation", StringComparison.Ordinal) ||
                !window.EditorPreviewText.Text.EndsWith("— Kyle", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The template editor label or personalized token preview did not render.");
            }
            if (window.TemplateList.IsEnabled)
                throw new InvalidOperationException("Changing templates must not discard an active template edit.");
        }
        else
        {
            AssertWithinRoot(window.PhoneBox, root, "Message recipient number");
            AssertWithinRoot(window.CopyNumberButton, root, "Copy recipient number");
            AssertWithinRoot(window.MessageBox, root, "Personalized message preview");
            AssertWithinRoot(window.CopyMessageButton, root, "Copy personalized message");
            var phoneLinkButton = FindVisualElements<Button>(window.ComposerPanel)
                                      .Single(button => AutomationProperties.GetName(button) == "Open Windows Phone Link");
            AssertWithinRoot(phoneLinkButton, root, "Open Phone Link");
            if (window.PhoneBox.Text != "(312) 555-0184" || !window.CopyNumberButton.IsEnabled ||
                !window.CopyMessageButton.IsEnabled ||
                !window.MessageBox.Text.Contains("Hi Amanda,", StringComparison.Ordinal) ||
                !window.MessageBox.Text.Contains("FF60R", StringComparison.Ordinal) ||
                !window.MessageBox.Text.Contains("Lakeview Renovation", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(window.PreviewFeedbackText.Text))
            {
                throw new InvalidOperationException("The composer did not render the personalized quote and phone number.");
            }
        }

        return new
        {
            width = root.ActualWidth,
            height = root.ActualHeight,
            templateCount = window.TemplateList.Items.Count,
            templateTextBindingCount = boundTemplateTexts.Length,
            editing,
            personalizedPreviewVerified = true,
            bindingErrorCount = 0
        };
    }

    private static object ValidateSpecLinkEditor(FrameworkElement root, MainViewModel viewModel, UrlVerificationRowVm row)
    {
        foreach (var control in FindVisualElements<Control>(root)
                     .Where(control => ReferenceEquals(control.DataContext, row) &&
                                       AutomationProperties.GetName(control) is "Resource name" or "Edit resource URL" or
                                                                                 "Save resource changes" or "Cancel resource changes"))
        {
            AssertWithinRoot(control, root, AutomationProperties.GetName(control));
            if (control is TextBox)
                AssertBindingHasNoError(control, TextBox.TextProperty, AutomationProperties.GetName(control));
            else if (control is Button)
            {
                AssertBindingHasNoError(control, Button.CommandProperty, AutomationProperties.GetName(control));
                AssertBindingHasNoError(control, Button.CommandParameterProperty, AutomationProperties.GetName(control));
            }
        }
        if (!row.IsEditing || !viewModel.HasUnsavedSpecLinkEdits || viewModel.CanCreateGmailDraft)
            throw new InvalidOperationException("An unsaved URL edit must remain visible and block draft creation.");
        return new { width = root.ActualWidth, height = root.ActualHeight, editingRowVisible = true, bindingErrorCount = 0 };
    }

    private static void PopulateRepresentativePhotos(MainViewModel viewModel, string snapshotDirectory)
    {
        viewModel.FireplacePhotoPaths.Clear();
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgr24, null, new byte[8 * 8 * 3], 8 * 3);
        foreach (var name in new[] { "Living-room-fireplace-wide-view.jpg", "Covered-patio-framing-detail.jpg" })
        {
            var path = Path.Combine(snapshotDirectory, name);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            viewModel.FireplacePhotoPaths.Add(path);
        }
    }

    private static object ValidatePhotoAttachments(MainWindow window, FrameworkElement root, MainViewModel viewModel)
    {
        var buttons = FindVisualElements<Button>(root)
                          .Where(button => ReferenceEquals(button.Command, viewModel.RemoveFireplacePhotoCommand))
                          .ToArray();
        if (buttons.Length != 2 || viewModel.FireplacePhotoItems.Any(photo => photo.IsMissing))
            throw new InvalidOperationException("The two representative photo attachments did not render.");
        var stageViewport = FindVisualElements<ScrollContentPresenter>(window.SpecLinksStageScroller).First();
        var photoViewport = FindVisualElements<ScrollContentPresenter>(window.FireplacePhotosScroller).First();
        var stageBounds = VisualBounds(stageViewport, root);
        var footerBounds = VisualBounds(window.SpecLinksActionFooter, root);
        if (stageBounds.Bottom > footerBounds.Top + 0.5)
            throw new InvalidOperationException("The resource review viewport overlaps its fixed action footer.");
        if (window.SpecLinksStageScroller.ExtentHeight > window.SpecLinksStageScroller.ViewportHeight + 0.5 &&
            window.SpecLinksStageScroller.ScrollableHeight <= 0)
            throw new InvalidOperationException("The photo list overflows without a usable stage scrollbar.");
        foreach (var button in buttons)
        {
            AssertWithinRoot(button, root, "Remove individual photo");
            AssertWithinViewport(button, stageViewport, root, "Remove photo within review viewport");
            AssertWithinViewport(button, photoViewport, root, "Remove photo within attachment viewport");
            var parent = VisualTreeHelper.GetParent(button);
            while (parent is not null && parent is not Border)
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is not FrameworkElement photoRow)
                throw new InvalidOperationException("The photo row container could not be located.");
            AssertWithinViewport(photoRow, stageViewport, root, "Photo row within review viewport");
            AssertWithinViewport(photoRow, photoViewport, root, "Photo row within attachment viewport");
            AssertBindingHasNoError(button, Button.CommandProperty, "Remove photo command");
            AssertBindingHasNoError(button, Button.CommandParameterProperty, "Remove photo parameter");
            if (button.CommandParameter is not PhotoAttachmentVm photo || button.Command?.CanExecute(photo) != true)
                throw new InvalidOperationException("Each photo must have a usable individual removal action.");
            AssertAutomationName(button, $"Remove photo {photo.FileName}");
        }
        return new
        {
            width = root.ActualWidth,
            height = root.ActualHeight,
            photoCount = buttons.Length,
            photosInsideViewports = true,
            stageScrollableHeight = window.SpecLinksStageScroller.ScrollableHeight,
            stageViewportBottom = stageBounds.Bottom,
            actionFooterTop = footerBounds.Top,
            bindingErrorCount = 0
        };
    }

    private static Rect VisualBounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static void AssertWithinViewport(FrameworkElement element, FrameworkElement viewport,
                                             FrameworkElement root, string name)
    {
        var allowed = VisualBounds(viewport, root);
        allowed.Inflate(0.5, 0.5);
        var bounds = VisualBounds(element, root);
        if (!allowed.Contains(bounds.TopLeft) || !allowed.Contains(bounds.BottomRight))
            throw new InvalidOperationException($"{name} is clipped outside the visible viewport: {bounds}.");
    }

    private static IEnumerable<T> FindVisualElements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matchingElement)
            yield return matchingElement;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in FindVisualElements<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static void ApplySnapshotTheme(MainWindow window, bool dark)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var guard = typeof(MainWindow).GetField("_isApplyingTheme", flags)
                        ?? throw new InvalidOperationException("The snapshot theme guard was not available.");
        // Change the visual toggle under its existing guard; the normal event handler must not
        // persist synthetic theme choices, even when this capture is launched outside the gate.
        guard.SetValue(window, true);
        try
        {
            window.ThemeToggleButton.IsChecked = dark;
        }
        finally
        {
            guard.SetValue(window, false);
        }
        var apply = typeof(MainWindow).GetMethod("ApplyTheme", flags)
                        ?? throw new InvalidOperationException("The snapshot theme method was not available.");
        apply.Invoke(window, [dark]);
        typeof(MainWindow).GetMethod("UpdateHeaderLogo", flags)?.Invoke(window, [dark]);
    }

    private static void AssertBindingHasNoError(DependencyObject element, DependencyProperty property, string name)
    {
        var binding = BindingOperations.GetBindingExpression(element, property);
        if (binding is null || binding.HasError)
            throw new InvalidOperationException($"{name} did not resolve without a binding error.");
    }

    private static void AssertWithinRoot(FrameworkElement element, FrameworkElement root, string name)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            throw new InvalidOperationException($"{name} did not render with a usable size.");

        var bounds = element.TransformToAncestor(root)
                            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var rootBounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);

        if (!rootBounds.Contains(bounds.TopLeft) || !rootBounds.Contains(bounds.BottomRight))
            throw new InvalidOperationException($"{name} rendered outside its root surface: {bounds}.");
    }

    private static void AssertRange(double actual, double minimum, double maximum, string name)
    {
        if (actual < minimum || actual > maximum)
            throw new InvalidOperationException($"{name} was {actual:F1}; expected {minimum:F1}–{maximum:F1}.");
    }

    private static int CountVisualText(DependencyObject root, string expectedText)
    {
        var count = root is TextBlock textBlock &&
                    string.Equals(textBlock.Text, expectedText, StringComparison.Ordinal)
                        ? 1
                        : 0;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            count += CountVisualText(VisualTreeHelper.GetChild(root, index), expectedText);

        return count;
    }

    private static void AssertAutomationName(DependencyObject element, string expectedName)
    {
        var actualName = AutomationProperties.GetName(element);
        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Accessibility name was '{actualName}' but expected '{expectedName}'.");
        }
    }

    private static void SaveVisual(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(visual);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX,
                                            dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
#endif
