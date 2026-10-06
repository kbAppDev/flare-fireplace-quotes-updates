using System.Windows;
using CommunityToolkit.Mvvm.Input;
using FlareQuotes.App.Views;
using FlareQuotes.Core.Messaging;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Security;

namespace FlareQuotes.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _useCompletedQuoteForMessages;

    public RelayCommand OpenTextMessageCommand { get; private set; } = null!;

    private void InitializeMessaging()
    {
        OpenTextMessageCommand = new RelayCommand(OpenTextMessage);
    }

    public TextMessageContext GetTextMessageContext()
    {
        var hasCurrentQuote = !string.IsNullOrWhiteSpace(ClientName) || !string.IsNullOrWhiteSpace(Phone) ||
                              !string.IsNullOrWhiteSpace(ProjectName) || !string.IsNullOrWhiteSpace(Email) ||
                              !string.IsNullOrWhiteSpace(RawRequest) || !string.IsNullOrWhiteSpace(Model) ||
                              !string.IsNullOrWhiteSpace(Postal) || !string.IsNullOrWhiteSpace(InstallDate) ||
                              !string.IsNullOrWhiteSpace(Size) || !string.IsNullOrWhiteSpace(GlassHeight) ||
                              !string.IsNullOrWhiteSpace(FireplaceLocation) ||
                              Fireplaces.Count > 0;
        var completed = !hasCurrentQuote && _useCompletedQuoteForMessages ? _lastCompletedQuoteSnapshot : null;
        var models = completed is not null
                         ? completed.Fireplaces.Select(fireplace => MessagingModelLabel(fireplace.Model, fireplace.Size))
                         : Fireplaces.Select(fireplace => MessagingModelLabel(fireplace.Model, fireplace.Size));
        var modelSummary = string.Join(", ", models.Where(model => !string.IsNullOrWhiteSpace(model))
                                                  .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(modelSummary) && hasCurrentQuote)
            modelSummary = MessagingModelLabel(Model, Size);
        else if (string.IsNullOrWhiteSpace(modelSummary) && completed is not null)
            modelSummary = MessagingModelLabel(completed.Model, completed.Size);

        var consultation = TrustedExternalLinkPolicy.TryNormalizeConsultation(_settings.ConsultationUrl, out var url) &&
                           !string.Equals(url.TrimEnd('/'), "https://flarefireplaces.com", StringComparison.OrdinalIgnoreCase)
                               ? url : TextMessageTemplateRenderer.DefaultConsultationUrl;
        return new TextMessageContext
        {
            ClientName = completed?.ClientName ?? ClientName,
            ProjectName = completed?.ProjectName ?? ProjectName,
            Phone = completed?.Phone ?? Phone,
            Model = modelSummary,
            ConsultationUrl = consultation,
            SourceLabel = completed is null ? "Current quote" : "Last quote"
        };
    }

    private static string MessagingModelLabel(string model, string size)
    {
        model = model.Trim();
        size = size.Trim();
        return string.IsNullOrWhiteSpace(size) || model.Contains(size, StringComparison.OrdinalIgnoreCase)
                   ? model : $"{model} {size}\"".Trim();
    }

    private void OpenTextMessage()
    {
        try
        {
            var dialog = new TextMessageWindow(GetTextMessageContext());
            if (Application.Current?.MainWindow is { } owner)
                dialog.Owner = owner;
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "The text message window could not be opened.");
            StatusMessage = FriendlyErrorMessage.FromException(exception,
                "The text message window could not open. Try again.");
        }
    }
}
