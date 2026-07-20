#if FLARE_UI_SNAPSHOTS
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlareQuotes.App.ViewModels;

namespace FlareQuotes.App.Views;

internal static class UiSnapshotCapture
{
    private const string SnapshotModeVariable = "FLARE_UI_SNAPSHOT_MODE";
    private const string SnapshotDirectoryVariable = "FLARE_UI_SNAPSHOT_DIR";

    public static async Task<bool> TryCaptureAsync(MainWindow mainWindow)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(SnapshotModeVariable), "1",
                           StringComparison.Ordinal))
        {
            return false;
        }

        var requestedDirectory = Environment.GetEnvironmentVariable(SnapshotDirectoryVariable);
        if (string.IsNullOrWhiteSpace(requestedDirectory))
            throw new InvalidOperationException($"{SnapshotDirectoryVariable} is required in snapshot mode.");

        var snapshotDirectory = Path.GetFullPath(requestedDirectory);
        Directory.CreateDirectory(snapshotDirectory);

        try
        {
            if (mainWindow.DataContext is not MainViewModel viewModel)
                throw new InvalidOperationException("The main window view model was not available.");

            PopulateRepresentativeQuote(viewModel);
            await RenderPendingLayoutAsync(mainWindow);

            var mainFrame = mainWindow.WindowFrame;
            mainWindow.Content = null;
            mainFrame.DataContext = viewModel;

            ArrangeAtSize(mainFrame, 1480, 920);
            var mainMetrics = ValidateMainWindow(mainWindow, mainFrame);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-dark.png"));

            ArrangeAtSize(mainFrame, 1180, 760);
            var minimumMainMetrics = ValidateMinimumMainWindow(mainWindow, mainFrame);
            SaveVisual(mainFrame, Path.Combine(snapshotDirectory, "main-window-minimum.png"));

            mainWindow.Content = mainFrame;

            var settingsWindow = new SettingsWindow {
                Owner = mainWindow,
                Width = 920,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            settingsWindow.Show();
            await RenderPendingLayoutAsync(settingsWindow);

            var settingsFrame = settingsWindow.SettingsFrame;
            settingsWindow.Content = null;

            ArrangeAtSize(settingsFrame, 920, 700);
            var settingsMetrics = ValidateSettingsWindow(settingsWindow, settingsFrame);
            SaveVisual(settingsFrame, Path.Combine(snapshotDirectory, "settings-window-dark.png"));

            ArrangeAtSize(settingsFrame, 820, 620);
            var minimumSettingsMetrics = ValidateSettingsWindow(settingsWindow, settingsFrame);
            SaveVisual(settingsFrame, Path.Combine(snapshotDirectory, "settings-window-minimum.png"));
            settingsWindow.Content = settingsFrame;
            settingsWindow.Close();

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

            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(snapshotDirectory, "snapshot-error.txt"), ex.ToString());
            Application.Current.Shutdown(1);
        }

        return true;
    }

    private static void PopulateRepresentativeQuote(MainViewModel viewModel)
    {
        viewModel.RawRequest =
            "Hi Flare team,\n\nPlease prepare a quote for the Jensen residence.\n\n" +
            "Project: Lakeview Renovation\nModel: Front Facing 60\"\nLocation: Living Room\n" +
            "Install target: September 2026\n\nCustomer: Amanda Jensen\n" +
            "amanda@jensenhome.com\n(312) 555-0184\n\n" +
            "Please include black reflective glass and standard lead time.";
        viewModel.ProjectName = "Lakeview Renovation";
        viewModel.ClientName = "Amanda Jensen";
        viewModel.Email = "amanda@jensenhome.com";
        viewModel.Phone = "(312) 555-0184";
        viewModel.Postal = "Chicago, IL 60614";
        viewModel.InstallDate = "September 2026";
        viewModel.Model = "Front Facing";
        viewModel.Size = "60";
        viewModel.GlassHeight = "16";
        viewModel.FireplaceLocation = "Living Room";
        viewModel.StatusMessage = "Customer and fireplace details detected.";

        var feature = viewModel.FilteredFeatureOptions.FirstOrDefault();
        if (feature is not null)
            viewModel.ToggleFeatureCommand.Execute(feature);

        var classicMedia = viewModel.FilteredClassicMediaOptions.FirstOrDefault();
        if (classicMedia is not null)
            viewModel.SelectClassicMediaCommand.Execute(classicMedia);
    }

    private static async Task RenderPendingLayoutAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Delay(200);
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void ArrangeAtSize(FrameworkElement root, double width, double height)
    {
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static object ValidateMainWindow(MainWindow window, FrameworkElement root)
    {
        AssertWithinRoot(window.RequestPane, root, nameof(window.RequestPane));
        AssertWithinRoot(window.QuoteWorkspacePane, root, nameof(window.QuoteWorkspacePane));
        AssertWithinRoot(window.GeneratePreviewButton, root, nameof(window.GeneratePreviewButton));
        AssertWithinRoot(window.ThemeToggleButton, root, nameof(window.ThemeToggleButton));

        AssertRange(window.RequestPane.ActualWidth, 380, 430, "Request pane width");
        AssertRange(window.QuoteWorkspacePane.ActualWidth, 780, 1100, "Quote workspace width");
        AssertRange(window.GeneratePreviewButton.ActualWidth, 178, 240, "Generate preview width");
        AssertRange(window.GeneratePreviewButton.ActualHeight, 34, 42, "Generate preview height");
        AssertRange(window.ThemeToggleButton.ActualWidth, 36, 40, "Theme button width");

        if (Application.Current.Windows.OfType<SystemHealthWindow>().Any())
            throw new InvalidOperationException("The system health window opened during normal startup.");

        return new {
            width = root.ActualWidth,
            height = root.ActualHeight,
            requestPaneWidth = window.RequestPane.ActualWidth,
            workspaceWidth = window.QuoteWorkspacePane.ActualWidth,
            generateButtonWidth = window.GeneratePreviewButton.ActualWidth,
            generateButtonHeight = window.GeneratePreviewButton.ActualHeight,
            themeButtonWidth = window.ThemeToggleButton.ActualWidth
        };
    }

    private static object ValidateSettingsWindow(SettingsWindow window, FrameworkElement root)
    {
        AssertWithinRoot(window.SettingsCancelButton, root, nameof(window.SettingsCancelButton));
        AssertWithinRoot(window.SettingsSaveButton, root, nameof(window.SettingsSaveButton));
        AssertRange(window.SettingsCancelButton.ActualHeight, 32, 36, "Settings cancel height");
        AssertRange(window.SettingsSaveButton.ActualHeight, 32, 36, "Settings save height");
        AssertRange(window.SettingsSaveButton.ActualWidth, 120, 150, "Settings save width");

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
        AssertWithinRoot(window.GeneratePreviewButton, root, nameof(window.GeneratePreviewButton));
        AssertRange(window.RequestPane.ActualWidth, 380, 430, "Minimum request pane width");
        AssertRange(window.QuoteWorkspacePane.ActualWidth, 680, 760, "Minimum quote workspace width");
        AssertRange(window.GeneratePreviewButton.ActualHeight, 34, 42, "Minimum generate preview height");

        return new {
            width = root.ActualWidth,
            height = root.ActualHeight,
            requestPaneWidth = window.RequestPane.ActualWidth,
            workspaceWidth = window.QuoteWorkspacePane.ActualWidth,
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
