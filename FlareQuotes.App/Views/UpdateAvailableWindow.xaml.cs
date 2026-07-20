using System;
using System.Windows;
using System.Windows.Input;

namespace FlareQuotes.App.Views
{
public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow() : this(string.Empty, string.Empty)
    {
    }

    public UpdateAvailableWindow(string? latestVersion) : this(latestVersion, string.Empty)
    {
    }

    public UpdateAvailableWindow(object? latestVersion) : this(latestVersion?.ToString(), string.Empty)
    {
    }

    public UpdateAvailableWindow(string? latestVersion, string? releaseNotes)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPresentationService.Apply(this, useDark: true);
        ApplyContent(latestVersion, releaseNotes);
    }

    public UpdateAvailableWindow(object? latestVersion, object? releaseNotes)
        : this(latestVersion?.ToString(), releaseNotes?.ToString())
    {
    }

    private void ApplyContent(string? latestVersion, string? releaseNotes)
    {
        var version = CleanVersion(latestVersion);

        VersionLineText.Text = string.IsNullOrWhiteSpace(version) ? "A new version is ready to install."
                                                                  : $"Version {version} is ready to install.";

        VersionBadgeText.Text = string.IsNullOrWhiteSpace(version) ? "Update" : $"v{version}";

        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(releaseNotes)
                                    ? "This update includes the latest fixes and improvements."
                                    : releaseNotes.Trim();
    }

    private static string CleanVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        return value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value[1..] : value;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
}
