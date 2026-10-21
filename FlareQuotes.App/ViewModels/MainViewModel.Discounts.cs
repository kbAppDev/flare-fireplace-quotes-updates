using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace FlareQuotes.App.ViewModels;

public sealed partial class MainViewModel
{
    private string _discountPercentText = string.Empty;
    private string _discountTerms = string.Empty;
    private long _quoteRevision;

    // Keep the entered text so invalid input cannot fall back to an earlier bound decimal value.
    public string DiscountPercentText
    {
        get => _discountPercentText;
        set
        {
            if (!SetProperty(ref _discountPercentText, value ?? string.Empty))
                return;
            NotifyDiscountStateChanged();
        }
    }

    public string DiscountTerms
    {
        get => _discountTerms;
        set
        {
            if (!SetProperty(ref _discountTerms, value ?? string.Empty))
                return;
            InvalidatePricedSnapshot();
        }
    }

    public bool HasInvalidCurrentDiscount => !TryGetCurrentDiscount(out _);
    public bool HasInvalidDiscount => HasInvalidCurrentDiscount ||
        Fireplaces.Any(fireplace => fireplace.DiscountPercent is < 0m or > 100m);
    public string DiscountValidationMessage => HasInvalidCurrentDiscount
        ? "Enter a discount from 0 to 100. Decimals are allowed; leave blank for no discount."
        : Fireplaces.Any(fireplace => fireplace.DiscountPercent is < 0m or > 100m)
            ? "A saved fireplace has a discount outside 0 to 100. Edit it before continuing."
            : string.Empty;

    private bool TryGetCurrentDiscount(out decimal percent)
    {
        percent = 0m;
        if (string.IsNullOrWhiteSpace(DiscountPercentText))
            return true;
        const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return (decimal.TryParse(DiscountPercentText, styles, CultureInfo.CurrentCulture, out percent) ||
                decimal.TryParse(DiscountPercentText, styles, CultureInfo.InvariantCulture, out percent)) &&
               percent is >= 0m and <= 100m;
    }

    private decimal CurrentDiscountPercent()
    {
        if (!TryGetCurrentDiscount(out var percent))
            throw new InvalidOperationException(DiscountValidationMessage);
        return percent;
    }

    private void NotifyDiscountStateChanged()
    {
        OnPropertyChanged(nameof(HasInvalidCurrentDiscount));
        OnPropertyChanged(nameof(HasInvalidDiscount));
        OnPropertyChanged(nameof(DiscountValidationMessage));
        AddFireplaceCommand?.NotifyCanExecuteChanged();
        NotifyFireplaceContextChanged();
    }

    private bool CanContinueQuoteWorkflow()
    {
        if (HasInvalidDiscount)
        {
            InvalidatePricedSnapshot();
            StatusMessage = DiscountValidationMessage;
            return false;
        }
        if (IsEditingFireplace || HasPendingNewFireplace)
        {
            StatusMessage = IsEditingFireplace
                ? "Save or cancel the fireplace changes before continuing."
                : "Add or clear the current fireplace before continuing.";
            return false;
        }
        return true;
    }

    public bool CanEditQuote => CreateDraftCommand?.IsRunning != true;

    // Command wrappers block queued/direct UI actions while allowing the successful draft's
    // internal capture-and-clear sequence to run before IsRunning returns to false.
    private void EditQuoteWhenIdle(Action action)
    {
        if (!CanEditQuote)
        {
            StatusMessage = "Wait for Gmail draft creation to finish before editing this quote.";
            return;
        }
        action();
    }

    private async Task EditQuoteWhenIdleAsync(Func<Task> action)
    {
        if (!CanEditQuote)
        {
            StatusMessage = "Wait for Gmail draft creation to finish before editing this quote.";
            return;
        }
        await action();
    }

    private void NotifyQuoteEditingAvailability()
    {
        OnPropertyChanged(nameof(CanEditQuote));
        OnPropertyChanged(nameof(CanGeneratePreview));
        IRelayCommand[] commands =
        [
            AutoFillCommand, ClearCommand, RecallLastQuoteCommand, RecallQuoteCommand,
            AddFireplaceCommand, ClearCurrentFireplaceCommand, RemoveFireplaceCommand, EditFireplaceCommand,
            DecreaseFireplaceQuantityCommand, IncreaseFireplaceQuantityCommand,
            MoveFireplaceUpCommand, MoveFireplaceDownCommand, NextToPreviewCommand, NextToSpecLinksCommand,
            RemoveFeatureCommand, RemovePremiumMediaCommand, RemoveAdditionalClassicMediaCommand,
            ToggleFeatureCommand, TogglePremiumMediaCommand, SelectClassicMediaCommand,
            SelectAdditionalClassicMediaCommand, ClearClassicMediaCommand, ClearAdditionalClassicMediaCommand,
            ClearFeaturesCommand, ClearPremiumMediaCommand, ClearLeadTimeCommand, SelectLeadTimeCommand
        ];
        foreach (var command in commands)
            command.NotifyCanExecuteChanged();
    }

    private void NavigateBackWhenIdle(QuoteWorkflowStage stage)
    {
        if (CreateDraftCommand.IsRunning)
        {
            StatusMessage = "Wait for Gmail draft creation to finish before editing this quote.";
            return;
        }
        WorkflowStage = stage;
    }

    private bool QuoteChangedDuringPreview(long revision)
    {
        if (revision == _quoteRevision && !HasInvalidDiscount)
            return false;
        InvalidatePricedSnapshot();
        StatusMessage = HasInvalidDiscount ? DiscountValidationMessage
            : "Quote details changed. Generate the preview again to use the current values.";
        return true;
    }
}
