#if FLARE_UI_SNAPSHOTS
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlareQuotes.App.ViewModels;

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

            ArrangeAtSize(mainFrame, 1480, 920);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-dark.png"));
            var mainMetrics = ValidateMainWindow(renderWindow, mainFrame, viewModel);

            ArrangeAtSize(mainFrame, 1180, 760);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-minimum.png"));
            var minimumMainMetrics = ValidateMinimumMainWindow(renderWindow, mainFrame);

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

            var metrics = new {
                generatedUtc = DateTime.UtcNow,
                mainWindow = mainMetrics,
                minimumMainWindow = minimumMainMetrics,
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

    private static object ValidateMainWindow(MainWindow window, FrameworkElement root, MainViewModel viewModel)
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

        if (window.ThemeToggleButton.IsChecked != true)
            throw new InvalidOperationException("The deterministic snapshot did not use the dark theme.");

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
