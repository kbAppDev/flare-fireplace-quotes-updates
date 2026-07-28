using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FlareQuotes.App.ViewModels;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FlareQuotes.App.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettingsService _settingsService;
        private readonly IGmailDraftService? _gmailDraftService;
        private AppSettings _settings = new();

        public SettingsWindow() : this(
            App.Services.GetRequiredService<ISettingsService>(),
            App.Services.GetRequiredService<IGmailDraftService>())
        {
        }

        internal SettingsWindow(ISettingsService settingsService) : this(settingsService, null)
        {
        }

        internal SettingsWindow(ISettingsService settingsService, IGmailDraftService? gmailDraftService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _gmailDraftService = gmailDraftService;
            InitializeComponent();
            Loaded += async (_, _) => await LoadSettingsIntoFormAsync();
        }

        private static string SettingsPath => AppPaths.SettingsFile;

        private async Task LoadSettingsIntoFormAsync()
        {
            try
            {
                _settings = await _settingsService.LoadAsync();

                SalesEmailBox.Text = _settings.SalesEmail ?? string.Empty;
                SalesPhoneBox.Text = _settings.SalesPhone ?? string.Empty;
                WebsiteBox.Text =
                    string.IsNullOrWhiteSpace(_settings.Website) ? "https://flarefireplaces.com" : _settings.Website;
                ConsultationUrlBox.Text = _settings.ConsultationUrl ?? string.Empty;
                HubSpotBccBox.Text = _settings.HubSpotBcc ?? string.Empty;
                GmailCredentialsPathBox.Text = _settings.GmailCredentialsPath ?? string.Empty;
                PricingFileBox.Text = _settings.PricingFile ?? string.Empty;
                UpdateManifestUrlBox.Text = _settings.UpdateManifestUrl ?? string.Empty;
                UseGmailSignatureCheckBox.IsChecked = _settings.UseGmailSignature;
                CheckUpdatesOnStartupCheckBox.IsChecked = _settings.CheckUpdatesOnStartup;
                RecallQuoteHistoryLimitBox.Text =
                    Math.Clamp(_settings.RecallQuoteHistoryLimit <= 0 ? 5 : _settings.RecallQuoteHistoryLimit, 1, 20)
                        .ToString();

                LeadTimePresetsBox.Text = string.Join(Environment.NewLine, (_settings.LeadTimePresets is { Count: > 0 }
                                                                                ? _settings.LeadTimePresets
                                                                                : new AppSettings().LeadTimePresets));

                SettingsStatusText.Text = $"Settings file: {SettingsPath}";
            }
            catch (Exception ex)
            {
                SettingsStatusText.Text = "Could not load settings: " + ex.Message;
            }
        }

        private async Task SaveFormToSettingsAsync()
        {
            _settings.SalesEmail = SalesEmailBox.Text.Trim();
            _settings.SalesPhone = SalesPhoneBox.Text.Trim();
            _settings.Website = WebsiteBox.Text.Trim();
            _settings.ConsultationUrl = ConsultationUrlBox.Text.Trim();
            _settings.HubSpotBcc = HubSpotBccBox.Text.Trim();
            _settings.GmailCredentialsPath = GmailCredentialsPathBox.Text.Trim();
            _settings.PricingFile = PricingFileBox.Text.Trim();
            _settings.UpdateManifestUrl = UpdateManifestUrlBox.Text.Trim();
            _settings.UseGmailSignature = UseGmailSignatureCheckBox.IsChecked == true;
            _settings.CheckUpdatesOnStartup = CheckUpdatesOnStartupCheckBox.IsChecked == true;
            if (!int.TryParse(RecallQuoteHistoryLimitBox.Text.Trim(), out var recallLimit))
                recallLimit = 5;

            _settings.RecallQuoteHistoryLimit = Math.Clamp(recallLimit, 1, 20);

            _settings.LeadTimePresets =
                LeadTimePresetsBox.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (_settings.LeadTimePresets.Count == 0)
                _settings.LeadTimePresets = new AppSettings().LeadTimePresets.ToList();

            await _settingsService.SaveAsync(_settings);

            PushSettingsIntoOwnerViewModel();
        }

        private void PushSettingsIntoOwnerViewModel()
        {
            try
            {
                if (Owner?.DataContext is MainViewModel viewModel)
                    viewModel.ApplySettings(_settings);
            }
            catch
            {
                // Settings were still saved to disk. Do not block the user over view-model refresh.
            }
        }

        private void BrowsePricingFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Pricing Workbook",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
                PricingFileBox.Text = dialog.FileName;
        }

        private void BrowseGmailCredentials_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Gmail Credentials JSON",
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
                GmailCredentialsPathBox.Text = dialog.FileName;
        }

        private void SettingsWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveFormToSettingsAsync();
                SettingsStatusText.Text = "Settings saved.";
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                // Inline status instead of a modal dialog, matching the rest of the app.
                SettingsStatusText.Text = "Save failed: " + ex.Message;
            }
        }

        private async void ReconnectGmail_Click(object sender, RoutedEventArgs e)
        {
            if (!ReconnectGmailButton.IsEnabled)
                return;

            var gmailService = _gmailDraftService ?? App.Services.GetService<IGmailDraftService>();
            if (gmailService is null)
            {
                GmailConnectionStatusText.Text = "Gmail service is unavailable on this machine.";
                return;
            }

            ReconnectGmailButton.IsEnabled = false;
            SettingsSaveButton.IsEnabled = false;

            try
            {
                GmailConnectionStatusText.Text = "Saving Gmail settings...";
                await SaveFormToSettingsAsync();

                GmailConnectionStatusText.Text = "Waiting for Google authorization in your browser...";
                var emailAddress = await gmailService.ReconnectAsync();

                GmailConnectionStatusText.Text = $"Connected as {emailAddress}.";
                SettingsStatusText.Text = "Gmail reconnected successfully.";
            }
            catch (OperationCanceledException)
            {
                GmailConnectionStatusText.Text = "Gmail reconnection was canceled.";
                SettingsStatusText.Text = "Gmail settings were saved, but authorization was not completed.";
            }
            catch
            {
                GmailConnectionStatusText.Text =
                    "Gmail could not reconnect. Confirm the credentials file, then try again.";
                SettingsStatusText.Text = "Gmail reconnection failed.";
            }
            finally
            {
                ReconnectGmailButton.IsEnabled = true;
                SettingsSaveButton.IsEnabled = true;
            }
        }
        private async void CheckForUpdatesNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatusText.Text = "Checking for updates...";

                var updateService = App.Services.GetService<IUpdateService>();
                if (updateService is null)
                {
                    UpdateStatusText.Text = "Update service is unavailable on this machine.";
                    return;
                }

                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var current = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";

                var result = await updateService.CheckAsync(current);

                if (result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.LatestVersion))
                    UpdateStatusText.Text =
                        $"Update available: {result.LatestVersion}. Close Settings and restart the app with startup update checks enabled to open the verified installer prompt.";
                else
                    UpdateStatusText.Text = $"You're on the latest version (v{current}).";
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Update check failed: " + ex.Message;
            }
        }
    }
}
