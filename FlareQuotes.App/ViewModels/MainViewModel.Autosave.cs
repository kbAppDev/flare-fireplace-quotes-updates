using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Paths;
using FlareQuotes.Core.Security;

namespace FlareQuotes.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly ProtectedJsonFileStore UnfinishedQuoteStore = new("Unfinished Quote");
    private static readonly JsonSerializerOptions UnfinishedQuoteJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IgnoreReadOnlyProperties = true
    };
    private static readonly HashSet<string> AutosavedProperties =
    [
        nameof(RawRequest), nameof(ProjectName), nameof(ClientName), nameof(Email), nameof(Phone),
        nameof(Postal), nameof(InstallDate), nameof(Model), nameof(Size), nameof(GlassHeight),
        nameof(FireplaceLocation), nameof(FireplaceQuantity), nameof(LeadTime), nameof(CustomLeadTime),
        nameof(ClassicMediaChoice), nameof(AdditionalClassicMediaChoice), nameof(IsEditingFireplace),
        nameof(ManualUrlToolName), nameof(ManualUrlValue), nameof(ManualPhotoAttachmentPath),
        nameof(SelectedUrlVerificationFireplace), nameof(WorkflowStage)
    ];

    private DispatcherTimer? _autosaveTimer;
    private string? _unfinishedQuotePath;
    private bool _restoringUnfinishedQuote;
    private bool _unfinishedQuoteDiscarded;
    private bool _autosaveFailureLogged;
    private readonly HashSet<SpecLinkDraft> _autosavedSpecLinkItems = [];
    private readonly HashSet<UrlVerificationRowVm> _autosavedSpecRowItems = [];

    private void InitializeAutosave()
    {
        // Unit fixtures create view models without a WPF application. UI snapshots use synthetic quotes.
        if (Application.Current is null ||
            string.Equals(Environment.GetEnvironmentVariable("FLARE_UI_SNAPSHOT_MODE"), "1",
                          StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            InitializeAutosaveForTesting(Path.Combine(AppPaths.Root, "unfinished_quote.json.dpapi"));
        }
        catch (Exception exception)
        {
            LogAutosaveFailure(exception, "initialize");
        }
    }

    private void InitializeAutosaveForTesting(string path)
    {
        if (_unfinishedQuotePath is not null)
            return;

        _unfinishedQuotePath = Path.GetFullPath(path);
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _autosaveTimer.Tick += (_, _) => SaveUnfinishedQuote();

        PropertyChanged += AutosavePropertyChanged;
        Fireplaces.CollectionChanged += AutosaveCollectionChanged;
        SelectedFeatures.CollectionChanged += AutosaveCollectionChanged;
        SelectedPremiumMedia.CollectionChanged += AutosaveCollectionChanged;
        SelectedAdditionalClassicMedia.CollectionChanged += AutosaveCollectionChanged;
        SpecLinks.CollectionChanged += AutosaveCollectionChanged;
        SelectedUrlVerificationRows.CollectionChanged += AutosaveCollectionChanged;
        FireplacePhotoPaths.CollectionChanged += AutosaveCollectionChanged;
        ReconcileAutosaveItemSubscriptions(SpecLinks, _autosavedSpecLinkItems);
        ReconcileAutosaveItemSubscriptions(SelectedUrlVerificationRows, _autosavedSpecRowItems);

        try
        {
            var state = UnfinishedQuoteStore.LoadOrMigrate<UnfinishedQuoteState>(
                _unfinishedQuotePath, options: UnfinishedQuoteJsonOptions);
            if (state is not null && state.Version == 1)
            {
                var missingPhotos = RestoreUnfinishedQuoteState(state);
                StatusMessage = missingPhotos == 0
                                    ? "Recovered your unfinished quote. Review it before generating the PDF."
                                    : $"Recovered your unfinished quote. {missingPhotos} saved photo attachment(s) are no longer available; select them again before drafting.";
            }
        }
        catch (Exception exception)
        {
            LogAutosaveFailure(exception, "restore");
        }
    }

    private void AutosavePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not null && AutosavedProperties.Contains(args.PropertyName))
            ScheduleUnfinishedQuoteSave();
    }

    private void AutosaveCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (ReferenceEquals(sender, SpecLinks))
            ReconcileAutosaveItemSubscriptions(SpecLinks, _autosavedSpecLinkItems);
        if (ReferenceEquals(sender, SelectedUrlVerificationRows))
            ReconcileAutosaveItemSubscriptions(SelectedUrlVerificationRows, _autosavedSpecRowItems);
        ScheduleUnfinishedQuoteSave();
    }

    private void ReconcileAutosaveItemSubscriptions<T>(IEnumerable<T> items, HashSet<T> observed)
        where T : INotifyPropertyChanged
    {
        var current = items.ToHashSet();
        foreach (var removed in observed.Where(item => !current.Contains(item)).ToList())
        {
            removed.PropertyChanged -= AutosaveItemPropertyChanged;
            observed.Remove(removed);
        }
        foreach (var added in current.Where(item => !observed.Contains(item)))
        {
            added.PropertyChanged += AutosaveItemPropertyChanged;
            observed.Add(added);
        }
    }

    private void AutosaveItemPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        ScheduleUnfinishedQuoteSave();

    private void ScheduleUnfinishedQuoteSave()
    {
        if (_unfinishedQuotePath is null || _restoringUnfinishedQuote)
            return;

        _unfinishedQuoteDiscarded = false;
        _autosaveTimer?.Stop();
        _autosaveTimer?.Start();
    }

    /// <summary>Flushes the current UI state before the window closes.</summary>
    public Task FlushAutosaveAsync()
    {
        // Capture and save on the UI thread. Debouncing keeps writes small and prevents queued writes
        // from restoring a quote after Clear or successful Gmail draft creation.
        SaveUnfinishedQuote();
        return Task.CompletedTask;
    }

    public void ClearUnfinishedQuote()
    {
        _autosaveTimer?.Stop();
        _unfinishedQuoteDiscarded = true;
        if (_unfinishedQuotePath is null)
            return;

        try
        {
            File.Delete(_unfinishedQuotePath);
            _autosaveFailureLogged = false;
        }
        catch (Exception exception)
        {
            LogAutosaveFailure(exception, "clear");
        }
    }

    private void SaveUnfinishedQuote()
    {
        _autosaveTimer?.Stop();
        if (_unfinishedQuotePath is null || _restoringUnfinishedQuote || _unfinishedQuoteDiscarded)
            return;

        try
        {
            var state = CaptureUnfinishedQuoteState();
            if (!HasUnfinishedQuoteContent(state))
                File.Delete(_unfinishedQuotePath);
            else
                UnfinishedQuoteStore.Save(_unfinishedQuotePath, state, UnfinishedQuoteJsonOptions);

            _autosaveFailureLogged = false;
        }
        catch (Exception exception)
        {
            LogAutosaveFailure(exception, "save");
        }
    }

    private void LogAutosaveFailure(Exception exception, string action)
    {
        if (_autosaveFailureLogged)
            return;

        _autosaveFailureLogged = true;
        _logger.Warning($"Unfinished quote {action} failed: {SafeForUser(exception.Message)}");
    }

    private static bool HasUnfinishedQuoteContent(UnfinishedQuoteState state) =>
        state.Fireplaces.Count > 0 || state.CurrentFeatures.Count > 0 || state.CurrentPremiumMedia.Count > 0 ||
        state.CurrentAdditionalClassicMedia.Count > 0 || state.SpecLinks.Count > 0 ||
        state.PhotoPaths.Count > 0 || state.CurrentClassicMedia is not null ||
        new[]
        {
            state.RawRequest, state.ProjectName, state.ClientName, state.Email, state.Phone, state.Postal,
            state.InstallDate, state.Model, state.Size, state.GlassHeight, state.FireplaceLocation,
            state.ManualUrlToolName, state.ManualUrlValue, state.ManualPhotoPath
        }.Any(value => !string.IsNullOrWhiteSpace(value));

    private UnfinishedQuoteState CaptureUnfinishedQuoteState() => new()
    {
        RawRequest = RawRequest,
        ProjectName = ProjectName,
        ClientName = ClientName,
        Email = Email,
        Phone = Phone,
        Postal = Postal,
        InstallDate = InstallDate,
        Model = Model,
        Size = Size,
        GlassHeight = GlassHeight,
        FireplaceLocation = FireplaceLocation,
        FireplaceQuantity = FireplaceQuantity,
        LeadTime = LeadTime,
        CustomLeadTime = CustomLeadTime,
        EditingFireplaceIndex = _editingFireplace is null ? -1 : Fireplaces.IndexOf(_editingFireplace),
        Fireplaces = Fireplaces.Select(CloneFireplaceDraftForRecall).ToList(),
        CurrentFeatures = SelectedFeatures.Select(Clone).ToList(),
        CurrentPremiumMedia = SelectedPremiumMedia.Select(Clone).ToList(),
        CurrentAdditionalClassicMedia = SelectedAdditionalClassicMedia.Select(Clone).ToList(),
        CurrentClassicMedia = ClassicMediaChoice is null ? null : new MediaSelection
        {
            Key = ClassicMediaChoice.Key,
            DisplayName = ClassicMediaChoice.DisplayName,
            IsPremium = false
        },
        SpecLinksReviewed = _specLinksReviewed,
        SpecLinks = SpecLinks.Select(CloneSpecLinkForAutosave).ToList(),
        SpecGroupMetadata = _specLinkGroupMetadata.Select(CloneSpecLinkForAutosave).ToList(),
        SelectedSpecGroupId = SelectedUrlVerificationFireplace?.GroupId ?? string.Empty,
        PendingSpecEdit = CapturePendingSpecEdit(),
        ManualUrlToolName = ManualUrlToolName,
        ManualUrlValue = ManualUrlValue,
        PhotoPaths = FireplacePhotoPaths.ToList(),
        ManualPhotoPath = ManualPhotoAttachmentPath
    };

    private PendingSpecLinkEdit? CapturePendingSpecEdit()
    {
        var row = SelectedUrlVerificationRows.FirstOrDefault(item => item.IsEditing);
        if (row?.SourceLink is not { } link)
            return null;
        return new PendingSpecLinkEdit
        {
            LinkIndex = SpecLinks.IndexOf(link),
            Label = row.EditingLabel,
            Url = row.EditingUrl
        };
    }

    private int RestoreUnfinishedQuoteState(UnfinishedQuoteState state)
    {
        _restoringUnfinishedQuote = true;
        try
        {
            EndFireplaceEdit();
            RawRequest = state.RawRequest;
            ProjectName = state.ProjectName;
            ClientName = state.ClientName;
            Email = state.Email;
            Phone = state.Phone;
            Postal = state.Postal;
            InstallDate = state.InstallDate;
            Model = state.Model;
            Size = state.Size;
            GlassHeight = state.GlassHeight;
            FireplaceLocation = state.FireplaceLocation;
            FireplaceQuantity = state.FireplaceQuantity;
            LeadTime = state.LeadTime;
            CustomLeadTime = state.CustomLeadTime;

            Fireplaces.Clear();
            foreach (var fireplace in state.Fireplaces)
                Fireplaces.Add(CloneFireplaceDraftForRecall(fireplace));

            RefreshSelectionOptions(preserveSelected: false);
            ClearFeatureSelections();
            foreach (var feature in state.CurrentFeatures)
                SetFeatureSelected(feature.Key, true);
            SelectedFeatures.Clear();
            foreach (var feature in state.CurrentFeatures)
                SelectedFeatures.Add(Clone(feature));

            ClearPremiumMediaSelections();
            foreach (var media in state.CurrentPremiumMedia)
                SetPremiumMediaSelected(media.Key, true);
            SelectedPremiumMedia.Clear();
            foreach (var media in state.CurrentPremiumMedia)
                SelectedPremiumMedia.Add(Clone(media));

            ClassicMediaChoice = state.CurrentClassicMedia is null ? null
                : ClassicMediaOptions.FirstOrDefault(option =>
                    string.Equals(option.Key, state.CurrentClassicMedia.Key, StringComparison.OrdinalIgnoreCase));
            RestoreAdditionalClassicMediaSelections(JoinAdditionalClassicMediaKeys(state.CurrentAdditionalClassicMedia));
            SelectedAdditionalClassicMedia.Clear();
            foreach (var media in state.CurrentAdditionalClassicMedia)
                SelectedAdditionalClassicMedia.Add(Clone(media));
            SyncAdditionalClassicMediaSelectionIndicator();

            _editingFireplace = state.EditingFireplaceIndex >= 0 && state.EditingFireplaceIndex < Fireplaces.Count
                                    ? Fireplaces[state.EditingFireplaceIndex] : null;
            OnPropertyChanged(nameof(IsEditingFireplace));
            OnPropertyChanged(nameof(AddFireplaceButtonText));

            _lastRequest = null;
            _lastPricedQuote = null;
            GeneratedPdfPath = string.Empty;
            QuotePreviewRows.Clear();
            WorkflowStage = QuoteWorkflowStage.Review;
            NotifyFireplaceContextChanged();
            OnPropertyChanged(nameof(FireplaceQuoteSummary));
            OnPropertyChanged(nameof(SelectedFeatureSummary));
            OnPropertyChanged(nameof(SelectedPremiumMediaSummary));
            OnPropertyChanged(nameof(ChargeableMediaSummary));
            UpdateStatusCards();

            // Restore reviewed resources last because rebuilding the editor can invalidate snapshots.
            _specLinkGroupMetadata.Clear();
            _specLinkGroupMetadata.AddRange(state.SpecGroupMetadata.Select(CloneSpecLinkForAutosave));
            SpecLinks.Clear();
            foreach (var link in state.SpecLinks)
                SpecLinks.Add(CloneSpecLinkForAutosave(link));
            _specLinksReviewed = state.SpecLinksReviewed;
            RefreshUrlVerificationCards();
            SelectedUrlVerificationFireplace = UrlVerificationFireplaces.FirstOrDefault(card =>
                string.Equals(card.GroupId, state.SelectedSpecGroupId, StringComparison.OrdinalIgnoreCase)) ??
                UrlVerificationFireplaces.FirstOrDefault();
            ManualUrlToolName = state.ManualUrlToolName;
            ManualUrlValue = state.ManualUrlValue;
            if (state.PendingSpecEdit is { } pending && pending.LinkIndex >= 0 &&
                pending.LinkIndex < SpecLinks.Count)
            {
                var row = SelectedUrlVerificationRows.FirstOrDefault(item =>
                    ReferenceEquals(item.SourceLink, SpecLinks[pending.LinkIndex]));
                if (row is not null)
                {
                    row.EditingLabel = pending.Label;
                    row.EditingUrl = pending.Url;
                    row.IsEditing = true;
                    NotifySpecEditState();
                }
            }

            FireplacePhotoPaths.Clear();
            var savedPhotos = state.PhotoPaths.Append(state.ManualPhotoPath)
                                  .Where(path => !string.IsNullOrWhiteSpace(path))
                                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var photo in savedPhotos.Where(File.Exists))
                FireplacePhotoPaths.Add(photo);
            ManualPhotoAttachmentPath = string.Empty;
            _unfinishedQuoteDiscarded = false;
            return savedPhotos.Count - FireplacePhotoPaths.Count;
        }
        finally
        {
            _restoringUnfinishedQuote = false;
        }
    }

    private static SpecLinkDraft CloneSpecLinkForAutosave(SpecLinkDraft source) => new()
    {
        FireplaceGroupId = source.FireplaceGroupId,
        FireplaceCode = source.FireplaceCode,
        FireplaceLocation = source.FireplaceLocation,
        Label = source.Label,
        Url = source.Url,
        Status = source.Status
    };

    private void EnrichRecallSnapshot(LastQuoteSnapshot snapshot) =>
        snapshot.WorkflowState = CaptureUnfinishedQuoteState();

    private void RestoreRecallWorkflow(LastQuoteSnapshot snapshot)
    {
        if (snapshot.WorkflowState is null)
            return;

        var missingPhotos = RestoreUnfinishedQuoteState(snapshot.WorkflowState);
        StatusMessage = missingPhotos == 0
                            ? $"Recalled quote for {snapshot.DisplayName}. Review it before generating the PDF."
                            : $"Recalled quote for {snapshot.DisplayName}. {missingPhotos} photo attachment(s) are no longer available; select them again before drafting.";
        ScheduleUnfinishedQuoteSave();
    }

    public sealed class UnfinishedQuoteState
    {
        public int Version { get; init; } = 1;
        public string RawRequest { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public string ClientName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Postal { get; init; } = string.Empty;
        public string InstallDate { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public string GlassHeight { get; init; } = string.Empty;
        public string FireplaceLocation { get; init; } = string.Empty;
        public int FireplaceQuantity { get; init; } = 1;
        public string LeadTime { get; init; } = "3-5 Business Days";
        public string CustomLeadTime { get; init; } = string.Empty;
        public int EditingFireplaceIndex { get; init; } = -1;
        public List<FireplaceQuoteDraft> Fireplaces { get; init; } = [];
        public List<FeatureSelection> CurrentFeatures { get; init; } = [];
        public List<MediaSelection> CurrentPremiumMedia { get; init; } = [];
        public List<MediaSelection> CurrentAdditionalClassicMedia { get; init; } = [];
        public MediaSelection? CurrentClassicMedia { get; init; }
        public bool SpecLinksReviewed { get; init; }
        public List<SpecLinkDraft> SpecLinks { get; init; } = [];
        public List<SpecLinkDraft> SpecGroupMetadata { get; init; } = [];
        public string SelectedSpecGroupId { get; init; } = string.Empty;
        public PendingSpecLinkEdit? PendingSpecEdit { get; init; }
        public string ManualUrlToolName { get; init; } = string.Empty;
        public string ManualUrlValue { get; init; } = string.Empty;
        public List<string> PhotoPaths { get; init; } = [];
        public string ManualPhotoPath { get; init; } = string.Empty;
    }

    public sealed class PendingSpecLinkEdit
    {
        public int LinkIndex { get; init; } = -1;
        public string Label { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }
}
