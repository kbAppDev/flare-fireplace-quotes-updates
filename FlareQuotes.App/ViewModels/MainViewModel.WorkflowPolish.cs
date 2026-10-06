using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlareQuotes.Core.Security;

namespace FlareQuotes.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string PendingSpecEditMessage = "Save or cancel the current link edit first.";
    private string _specLinkValidationMessage = string.Empty;

    public RelayCommand<FireplaceQuoteDraft> DuplicateFireplaceCommand { get; private set; } = null!;
    public RelayCommand<PhotoAttachmentVm> RemoveFireplacePhotoCommand { get; private set; } = null!;
    public RelayCommand<UrlVerificationRowVm> EditSpecLinkCommand { get; private set; } = null!;
    public RelayCommand<UrlVerificationRowVm> SaveSpecLinkCommand { get; private set; } = null!;
    public RelayCommand<UrlVerificationRowVm> CancelSpecLinkEditCommand { get; private set; } = null!;

    public string SpecLinkValidationMessage
    {
        get => _specLinkValidationMessage;
        private set => SetProperty(ref _specLinkValidationMessage, value);
    }

    public bool HasUnsavedSpecLinkEdits => SelectedUrlVerificationRows.Any(row => row.IsEditing);
    public IReadOnlyList<PhotoAttachmentVm> FireplacePhotoItems =>
        FireplacePhotoPaths.Select(path => new PhotoAttachmentVm(path)).ToList();

    public string PhotoAttachmentSizeText
    {
        get
        {
            var bytes = FireplacePhotoItems.Sum(item => item.SizeBytes);
            return bytes == 0 ? string.Empty : bytes < 1048576
                ? $"{Math.Max(1, bytes / 1024):0} KB" : $"{bytes / 1048576d:0.#} MB";
        }
    }

    private void InitializeWorkflowPolish()
    {
        DuplicateFireplaceCommand = new RelayCommand<FireplaceQuoteDraft>(DuplicateFireplace,
            fireplace => fireplace is not null && Fireplaces.Contains(fireplace) &&
                         !IsEditingFireplace && !HasPendingNewFireplace && !CreateDraftCommand.IsRunning);
        RemoveFireplacePhotoCommand = new RelayCommand<PhotoAttachmentVm>(RemoveFireplacePhoto,
            photo => photo is not null && FireplacePhotoPaths.Contains(photo.Path) && CanEditSpecLinks);
        EditSpecLinkCommand = new RelayCommand<UrlVerificationRowVm>(EditSpecLink,
            row => CanEditSpecRow(row) && !HasUnsavedSpecLinkEdits);
        SaveSpecLinkCommand = new RelayCommand<UrlVerificationRowVm>(SaveSpecLink,
            row => CanEditSpecRow(row) && row!.IsEditing);
        CancelSpecLinkEditCommand = new RelayCommand<UrlVerificationRowVm>(CancelSpecLinkEdit,
            row => CanEditSpecRow(row) && row!.IsEditing);

        FireplacePhotoPaths.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FireplacePhotoItems));
            OnPropertyChanged(nameof(PhotoAttachmentSizeText));
            RemoveFireplacePhotoCommand.NotifyCanExecuteChanged();
        };
        Fireplaces.CollectionChanged += (_, _) => DuplicateFireplaceCommand.NotifyCanExecuteChanged();
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsEditingFireplace) or nameof(HasPendingNewFireplace))
                DuplicateFireplaceCommand.NotifyCanExecuteChanged();
        };
        CreateDraftCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(CreateDraftCommand.IsRunning))
                return;
            DuplicateFireplaceCommand.NotifyCanExecuteChanged();
            RemoveFireplacePhotoCommand.NotifyCanExecuteChanged();
            NotifySpecEditState();
        };
    }

    private void DuplicateFireplace(FireplaceQuoteDraft? original)
    {
        if (original is null || !DuplicateFireplaceCommand.CanExecute(original))
            return;
        var duplicate = CloneFireplaceDraftForRecall(original);
        var location = string.IsNullOrWhiteSpace(original.Location) ? "Fireplace" : original.Location.Trim();
        var copyNumber = 1;
        do
        {
            duplicate.Location = copyNumber == 1 ? $"{location} (copy)" : $"{location} (copy {copyNumber})";
            copyNumber++;
        } while (Fireplaces.Any(fireplace => string.Equals(fireplace.Location, duplicate.Location,
                                                          StringComparison.OrdinalIgnoreCase)));
        Fireplaces.Insert(Fireplaces.IndexOf(original) + 1, duplicate);
        InvalidatePricedSnapshot();
        EditFireplace(duplicate);
        StatusMessage = $"Duplicated {original.DisplayName}.";
    }

    private void RemoveFireplacePhoto(PhotoAttachmentVm? photo)
    {
        if (photo is null || !CanEditSpecLinks)
            return;
        var path = FireplacePhotoPaths.FirstOrDefault(value =>
            string.Equals(value, photo.Path, StringComparison.OrdinalIgnoreCase));
        if (path is null || !FireplacePhotoPaths.Remove(path))
            return;
        if (string.Equals(ManualPhotoAttachmentPath, path, StringComparison.OrdinalIgnoreCase))
            ManualPhotoAttachmentPath = string.Empty;
        StatusMessage = $"Removed {photo.FileName}.";
    }

    private bool CanEditSpecRow(UrlVerificationRowVm? row) =>
        CanEditSpecLinks && row?.SourceLink is { } link && SpecLinks.Contains(link) &&
        SelectedUrlVerificationRows.Contains(row);

    private bool TryChangeSpecReview()
    {
        if (!CanEditSpecLinks)
            return false;
        if (!HasUnsavedSpecLinkEdits)
            return true;
        SpecLinkValidationMessage = PendingSpecEditMessage;
        StatusMessage = PendingSpecEditMessage;
        return false;
    }

    private void EditSpecLink(UrlVerificationRowVm? row)
    {
        if (!CanEditSpecRow(row) || !TryChangeSpecReview() || row?.SourceLink is not { } link)
            return;
        row.EditingLabel = link.Label;
        row.EditingUrl = link.Url;
        row.ValidationMessage = string.Empty;
        row.IsEditing = true;
        SpecLinkValidationMessage = PendingSpecEditMessage;
        StatusMessage = $"Editing '{link.Label}'. Save or cancel before changing other URLs.";
        NotifySpecEditState();
    }

    private void CancelSpecLinkEdit(UrlVerificationRowVm? row)
    {
        if (!CanEditSpecRow(row) || row is null || !row.IsEditing)
            return;
        row.IsEditing = false;
        row.ValidationMessage = string.Empty;
        SpecLinkValidationMessage = string.Empty;
        StatusMessage = "Link edit canceled.";
        NotifySpecEditState();
    }

    private void SaveSpecLink(UrlVerificationRowVm? row)
    {
        if (!CanEditSpecRow(row) || row is not { IsEditing: true, SourceLink: { } link })
            return;
        if (!TryValidateSpecLink(row.EditingLabel, row.EditingUrl, link.FireplaceGroupId, link,
                                 out var label, out var url, out var error))
        {
            row.ValidationMessage = error;
            StatusMessage = error;
            return;
        }
        link.Label = label;
        link.Url = url;
        link.Status = "manual";
        row.IsEditing = false;
        SpecLinkValidationMessage = string.Empty;
        RefreshUrlVerificationCards();
        NotifySpecEditState();
        StatusMessage = $"Updated '{label}'.";
    }

    private bool TryValidateSpecLink(string? enteredLabel, string? enteredUrl, string groupId,
                                     SpecLinkDraft? existing, out string label, out string url,
                                     out string error)
    {
        label = (enteredLabel ?? string.Empty).Trim();
        var normalizedLabel = label;
        url = string.Empty;
        error = string.Empty;
        if (label.Length is 0 or > 120)
            error = "Enter a link name between 1 and 120 characters.";
        else if (SpecLinks.Any(link => !ReferenceEquals(link, existing) &&
                                     string.Equals(link.FireplaceGroupId, groupId, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(link.Label.Trim(), normalizedLabel, StringComparison.OrdinalIgnoreCase)))
            error = $"This fireplace already has a link named '{label}'. Edit that link or choose another name.";
        else if (!TrustedExternalLinkPolicy.TryNormalize(enteredUrl, out url))
            error = "Enter an approved HTTPS link from Flare Fireplaces or Flare Order.";
        return error.Length == 0;
    }

    private void NotifySpecEditState()
    {
        if (!HasUnsavedSpecLinkEdits && SpecLinkValidationMessage == PendingSpecEditMessage)
            SpecLinkValidationMessage = string.Empty;
        OnPropertyChanged(nameof(HasUnsavedSpecLinkEdits));
        OnPropertyChanged(nameof(CanCreateGmailDraft));
        OnPropertyChanged(nameof(GmailDraftRequirementText));
        CreateDraftCommand.NotifyCanExecuteChanged();
        SelectUrlVerificationFireplaceCommand.NotifyCanExecuteChanged();
        AddManualUrlCommand.NotifyCanExecuteChanged();
        RemoveSpecLinkCommand.NotifyCanExecuteChanged();
        EditSpecLinkCommand?.NotifyCanExecuteChanged();
        SaveSpecLinkCommand?.NotifyCanExecuteChanged();
        CancelSpecLinkEditCommand?.NotifyCanExecuteChanged();
    }
}

public sealed class PhotoAttachmentVm
{
    public PhotoAttachmentVm(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        try
        {
            var file = new FileInfo(path);
            SizeBytes = file.Exists ? file.Length : 0;
            IsMissing = !file.Exists;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            IsMissing = true;
        }
    }

    public string Path { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public bool IsMissing { get; }
    public string SizeText => IsMissing ? "Unavailable" : SizeBytes < 1048576
        ? $"{Math.Max(1, SizeBytes / 1024):0} KB" : $"{SizeBytes / 1048576d:0.#} MB";
}
