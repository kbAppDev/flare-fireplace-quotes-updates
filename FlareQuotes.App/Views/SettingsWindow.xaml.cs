using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;

namespace FlareQuotes.App.Views
{
public partial class SettingsWindow : Window
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private AppSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        { LoadSettingsIntoForm(); };
    }

    private static string SettingsPath => AppPaths.SettingsFile;

    private void LoadSettingsIntoForm()
    {
        try
        {
            _settings = LoadSettings();

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

            SelectEmailSendMode(_settings.EmailSendMode);

            LeadTimePresetsBox.Text = string.Join(Environment.NewLine, (_settings.LeadTimePresets is { Count : > 0 }
                                                                            ? _settings.LeadTimePresets
                                                                            : new AppSettings().LeadTimePresets));

            SettingsStatusText.Text = $"Settings file: {SettingsPath}";
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "Could not load settings: " + ex.Message;
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void SaveFormToSettings()
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
        _settings.EmailSendMode = SelectedEmailSendMode();
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

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        var json = JsonSerializer.Serialize(_settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json);

        PushSettingsIntoOwnerViewModel();
    }

    private void PushSettingsIntoOwnerViewModel()
    {
        try
        {
            var vm = Owner?.DataContext;
            if (vm is null)
                return;

            var vmType = vm.GetType();

            var settingsField = vmType.GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
            if (settingsField?.GetValue(vm) is AppSettings liveSettings)
            {
                CopySettings(_settings, liveSettings);
            }

            var leadTimeProperty = vmType.GetProperty("LeadTimePresets", BindingFlags.Instance | BindingFlags.Public);
            if (leadTimeProperty?.GetValue(vm) is IList leadTimes)
            {
                leadTimes.Clear();
                foreach (var item in _settings.LeadTimePresets)
                    leadTimes.Add(item);
            }

            var onPropertyChanged = vmType.GetMethod(
                "OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { typeof(string) }, null);
            onPropertyChanged?.Invoke(vm, new object[] { "LeadTimePresets" });
            onPropertyChanged?.Invoke(vm, new object[] { "LeadTimeDropdownButtonText" });
        }
        catch
        {
            // Settings were still saved to disk. Do not block the user over view-model refresh.
        }
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.SalesEmail = source.SalesEmail;
        target.SalesPhone = source.SalesPhone;
        target.Website = source.Website;
        target.HubSpotBcc = source.HubSpotBcc;
        target.ConsultationUrl = source.ConsultationUrl;
        target.PricingFile = source.PricingFile;
        target.LastSaveDir = source.LastSaveDir;
        target.UseGmailSignature = source.UseGmailSignature;
        target.EmailSendMode = source.EmailSendMode;
        target.CheckUpdatesOnStartup = source.CheckUpdatesOnStartup;
        target.UpdateManifestUrl = source.UpdateManifestUrl;
        target.GmailCredentialsPath = source.GmailCredentialsPath;
        target.LeadTimePresets = source.LeadTimePresets.ToList();
    }

    private void SelectEmailSendMode(string? mode)
    {
        var target = string.IsNullOrWhiteSpace(mode) ? "draft" : mode.Trim();

        for (var i = 0; i < EmailSendModeBox.Items.Count; i++)
        {
            if (EmailSendModeBox.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                EmailSendModeBox.SelectedIndex = i;
                return;
            }
        }

        EmailSendModeBox.SelectedIndex = 0;
    }

    private string SelectedEmailSendMode()
    {
        if (EmailSendModeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            return item.Content?.ToString() ?? "draft";

        return "draft";
    }

    private void BrowsePricingFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select Pricing Workbook",
                                          Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*" };

        if (dialog.ShowDialog(this) == true)
            PricingFileBox.Text = dialog.FileName;
    }

    private void BrowseGmailCredentials_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select Gmail Credentials JSON",
                                          Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*" };

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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveFormToSettings();
            SettingsStatusText.Text = "Settings saved.";
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "Save failed: " + ex.Message;
            MessageBox.Show("Settings could not be saved." + Environment.NewLine + Environment.NewLine + ex.Message,
                            "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
}
