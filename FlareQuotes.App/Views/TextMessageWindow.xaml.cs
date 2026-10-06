using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlareQuotes.Core.Messaging;
using FlareQuotes.Core.Models;

namespace FlareQuotes.App.Views;

public partial class TextMessageWindow : Window
{
    private readonly TextMessageContext _context;
    private readonly TextMessageTemplateStore _store;
    private readonly TextMessageTemplateLibrary _library;
    private readonly ObservableCollection<TextMessageTemplate> _templates = [];
    private bool _initializing = true;
    private bool _updatingPreview;
    private bool _previewCustomized;
    private bool _preferencesChanged;
    private string? _editingTemplateId;
    private TextMessageTemplate? _deletedTemplate;

    public TextMessageWindow(TextMessageContext context, TextMessageTemplateStore? store = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _store = store ?? new TextMessageTemplateStore();
        _library = _store.Load();
        InitializeComponent();
        WindowAppearance.Attach(this);
        if (TryFindResource("FlareTextBrush") is SolidColorBrush textBrush)
            WindowAppearance.UpdateTheme(this, textBrush.Color.R > 128);

        TemplateList.ItemsSource = _templates;
        SalesNameBox.Text = string.IsNullOrWhiteSpace(_library.SalesName) ? context.SalesName : _library.SalesName;
        PhoneBox.Text = context.Phone;
        var details = string.Join(" · ", new[] { context.ClientName, context.ProjectName }
                                           .Where(value => !string.IsNullOrWhiteSpace(value)));
        QuoteContextText.Text = string.IsNullOrWhiteSpace(details) ? "Write a message" : $"{context.SourceLabel} · {details}";
        RefreshTemplateList();
        _initializing = false;
        RefreshPhoneState();
        RefreshPreview(force: true);
    }

    private TextMessageContext CurrentContext => _context with { SalesName = SalesNameBox.Text.Trim() };
    private TextMessageTemplate? SelectedTemplate => TemplateList.SelectedItem as TextMessageTemplate;
    private bool IsEditing => EditorPanel.Visibility == Visibility.Visible;

    private void RefreshTemplateList()
    {
        var wasInitializing = _initializing;
        _initializing = true;
        _templates.Clear();
        foreach (var template in _library.Templates)
            _templates.Add(template);
        TemplateList.SelectedItem = _templates.FirstOrDefault(template => template.Id == _library.SelectedTemplateId)
                                        ?? _templates.FirstOrDefault();
        _initializing = wasInitializing;
        UpdateManagementButtons();
    }

    private void UpdateManagementButtons()
    {
        TemplateList.IsEnabled = !IsEditing;
        AddTemplateButton.IsEnabled = !IsEditing;
        EditTemplateButton.IsEnabled = !IsEditing && SelectedTemplate is not null;
        DeleteTemplateButton.IsEnabled = !IsEditing && SelectedTemplate is not null;
        UndoDeleteButton.IsEnabled = !IsEditing;
    }

    private void RefreshPreview(bool force = false)
    {
        if (_initializing || _previewCustomized && !force)
            return;

        var rendered = TextMessageTemplateRenderer.Render(SelectedTemplate?.Body, CurrentContext);
        _updatingPreview = true;
        MessageBox.Text = rendered.Text;
        _updatingPreview = false;
        _previewCustomized = false;
        PreviewFeedbackText.Text = TokenFeedback(rendered);
        RefreshMessageState();
    }

    private static string TokenFeedback(TextMessageRenderResult rendered)
    {
        if (rendered.UnknownTokens.Count > 0)
            return "Unknown tokens: " + string.Join(", ", rendered.UnknownTokens.Select(token => $"{{{token}}}"));
        if (rendered.MissingValues.Count > 0)
            return "Missing details: " + string.Join(", ", rendered.MissingValues) + ". Review the preview.";
        return string.Empty;
    }

    private void RefreshMessageState()
    {
        if (CopyMessageButton is null)
            return;
        CopyMessageButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageBox.Text);
        OpenPhoneLinkButton.IsEnabled = CopyMessageButton.IsEnabled;
        CharacterCountText.Text = $"{MessageBox.Text.Length:N0} characters";
    }

    private void RefreshPhoneState()
    {
        if (CopyNumberButton is null)
            return;
        var valid = PhoneLinkMessageHelper.TryNormalizePhone(PhoneBox.Text, out _);
        CopyNumberButton.IsEnabled = valid;
        PhoneStatusText.Text = !valid && !string.IsNullOrWhiteSpace(PhoneBox.Text)
                                   ? "Check the phone number." : string.Empty;
    }

    private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        _library.SelectedTemplateId = SelectedTemplate?.Id ?? string.Empty;
        _preferencesChanged = true;
        RefreshPreview(force: true);
        UpdateManagementButtons();
    }

    private void SalesNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
            return;
        _preferencesChanged = true;
        RefreshPreview();
        RefreshEditorPreview();
    }

    private void SalesNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initializing)
            SavePreferences();
    }

    private void MessageBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing || _updatingPreview)
            return;
        _previewCustomized = true;
        var rendered = TextMessageTemplateRenderer.Render(MessageBox.Text, CurrentContext);
        PreviewFeedbackText.Text = rendered.UnknownTokens.Count > 0
                                      ? TokenFeedback(rendered) : "Customized for this message.";
        RefreshMessageState();
    }

    private void PhoneBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing)
            RefreshPhoneState();
    }

    private void ResetPreview_Click(object sender, RoutedEventArgs e) => RefreshPreview(force: true);

    private void UseQuoteNumber_Click(object sender, RoutedEventArgs e) => PhoneBox.Text = _context.Phone;

    private void BeginTemplateEdit(TextMessageTemplate? template)
    {
        _editingTemplateId = template?.Id;
        EditorPanel.Visibility = Visibility.Visible;
        ComposerPanel.Visibility = Visibility.Collapsed;
        RecipientPanel.Visibility = Visibility.Collapsed;
        EditorHeadingText.Text = template is null ? "New template" : "Edit template";
        TemplateNameBox.Text = template?.Name ?? string.Empty;
        TemplateBodyBox.Text = template?.Body ?? string.Empty;
        RefreshEditorPreview();
        UpdateManagementButtons();
        TemplateNameBox.Focus();
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e) => BeginTemplateEdit(null);

    private void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is { } template)
            BeginTemplateEdit(template);
    }

    private void EndTemplateEdit()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        ComposerPanel.Visibility = Visibility.Visible;
        RecipientPanel.Visibility = Visibility.Visible;
        _editingTemplateId = null;
        UpdateManagementButtons();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => EndTemplateEdit();

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _library.SalesName = SalesNameBox.Text.Trim();
            _store.SaveTemplate(_library, _editingTemplateId, TemplateNameBox.Text, TemplateBodyBox.Text);
            _preferencesChanged = false;
            EndTemplateEdit();
            RefreshTemplateList();
            RefreshPreview(force: true);
            MessageStatusText.Text = "Template saved.";
        }
        catch (ArgumentException exception)
        {
            EditorStatusText.Text = exception.Message;
        }
        catch (Exception)
        {
            EditorStatusText.Text = "The template could not be saved. Try again.";
        }
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is not { } template)
            return;
        try
        {
            _deletedTemplate = _store.DeleteTemplate(_library, template.Id);
            UndoDeleteButton.Visibility = Visibility.Visible;
            RefreshTemplateList();
            RefreshPreview(force: true);
            MessageStatusText.Text = "Template deleted. Undo is available.";
        }
        catch (Exception)
        {
            MessageStatusText.Text = "The template could not be deleted. Try again.";
        }
    }

    private void UndoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_deletedTemplate is null)
            return;
        try
        {
            _store.RestoreTemplate(_library, _deletedTemplate);
            _deletedTemplate = null;
            UndoDeleteButton.Visibility = Visibility.Collapsed;
            RefreshTemplateList();
            RefreshPreview(force: true);
            MessageStatusText.Text = "Template restored.";
        }
        catch (Exception)
        {
            MessageStatusText.Text = "The template could not be restored. Check for a duplicate name.";
        }
    }

    private void TemplateBodyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing)
            RefreshEditorPreview();
    }

    private void RefreshEditorPreview()
    {
        if (!IsEditing)
            return;
        var rendered = TextMessageTemplateRenderer.Render(TemplateBodyBox.Text, CurrentContext);
        EditorPreviewText.Text = rendered.Text;
        EditorStatusText.Text = string.IsNullOrWhiteSpace(rendered.Text) ? "Template is empty." : TokenFeedback(rendered);
    }

    private void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token })
            return;
        var start = TemplateBodyBox.SelectionStart;
        var insertion = $"{{{token}}}";
        TemplateBodyBox.SelectedText = insertion;
        TemplateBodyBox.CaretIndex = start + insertion.Length;
        TemplateBodyBox.SelectionLength = 0;
        TemplateBodyBox.Focus();
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MessageBox.Text))
            return;
        try
        {
            Clipboard.SetText(MessageBox.Text);
            MessageStatusText.Text = "Message copied.";
            SavePreferences();
        }
        catch (Exception)
        {
            MessageStatusText.Text = "The clipboard is busy. Try copying again.";
        }
    }

    private void CopyNumber_Click(object sender, RoutedEventArgs e)
    {
        if (!PhoneLinkMessageHelper.TryNormalizePhone(PhoneBox.Text, out var number))
            return;
        try
        {
            Clipboard.SetText(number);
            MessageStatusText.Text = "Number copied.";
        }
        catch (Exception)
        {
            MessageStatusText.Text = "The clipboard is busy. Try copying again.";
        }
    }

    private void OpenPhoneLink_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MessageBox.Text))
            return;
        try
        {
            Clipboard.SetText(MessageBox.Text);
        }
        catch (Exception)
        {
            MessageStatusText.Text = "The clipboard is busy. Try again.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(PhoneLinkMessageHelper.LaunchUri) { UseShellExecute = true });
            MessageStatusText.Text = "Message copied. Phone Link opened.";
            SavePreferences();
        }
        catch (Exception)
        {
            MessageStatusText.Text = "Message copied. Phone Link could not open. Check that it is installed and connected.";
        }
    }

    private void SavePreferences()
    {
        if (!_preferencesChanged)
            return;
        try
        {
            _library.SalesName = SalesNameBox.Text.Trim();
            _library.SelectedTemplateId = SelectedTemplate?.Id ?? string.Empty;
            _store.Save(_library);
            _preferencesChanged = false;
        }
        catch (Exception)
        {
            MessageStatusText.Text = "Your template preferences could not be saved. Message edits remain available.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (IsEditing)
            EndTemplateEdit();
        else
            Close();
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePreferences();
        base.OnClosed(e);
    }
}
