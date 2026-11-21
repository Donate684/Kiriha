using Kiriha.Core.Domain.Utils.Parsing;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.ViewModels.Settings;

namespace Kiriha.ViewModels.AnimeDetails;


public partial class AnimeDetailsViewModel : ViewModelBase, IDisposable
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _animePropertyChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private AnimeEntity _anime;

    public string WindowTitle
    {
        get
        {
            if (Anime == null) return "Kiriha";
            var useRussian = Anime.Presentation.EffectiveUseRussianTitles;
            var preferred = useRussian
                ? (!string.IsNullOrWhiteSpace(Anime.RussianTitle) ? Anime.RussianTitle : Anime.Title)
                : (!string.IsNullOrWhiteSpace(Anime.Title) ? Anime.Title : Anime.RussianTitle);

            return !string.IsNullOrWhiteSpace(preferred) ? preferred : "Kiriha";
        }
    }

    [ObservableProperty]
    private AnimeEditViewModel _editor;

    [ObservableProperty]
    private AnimeMetadataViewModel _metadata;

    [ObservableProperty]
    private bool _canGoBack;

    public bool HasAnySavedChanges { get; private set; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPosterPreviewOpen;


    public System.Collections.ObjectModel.ObservableCollection<RelationItemVm> Relations { get; } = new();

    [ObservableProperty]
    private bool _hasFranchiseTimeline;

    [ObservableProperty]
    private bool _hasStandardRelations;

    [ObservableProperty]
    private bool _hasAnyRelationsOrTimeline;

    [ObservableProperty]
    private int _franchiseCompletedCount;

    [ObservableProperty]
    private int _franchiseTotalCount;

    [ObservableProperty]
    private int _franchiseCompletionPercentage;

    [ObservableProperty]
    private string _franchiseProgressSummary = string.Empty;

    [ObservableProperty]
    private string _franchiseCompletionPercentText = string.Empty;

    [ObservableProperty]
    private string _franchiseProgressTooltip = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<Kiriha.Utils.Graphs.FranchiseGraphVisualNode> FranchiseTimeline { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private AnimeEntity _originalAnime;
    private readonly IAnimeListActionService _listActionService;
    private readonly Stack<AnimeEntity> _navigationHistory = new();
    private bool _disposed;

    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly IShikiApiService _shikiApiService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApiService;
    private readonly IFranchiseService _franchiseService;
    private readonly ILocalizer _localizer;

    public ISettingsService Settings => _settingsService;

    public AnimeDetailsViewModel(
        AnimeEntity cloneAnime,
        AnimeEntity originalAnime,
        AnimeEditViewModel editor,
        AnimeMetadataViewModel metadata,
        ISettingsService settingsService,
        IDialogService dialogs,
        IShikiApiService shikiApiService,
        IAnimeRepository animeRepo,
        IMalApiService malApiService,
        IFranchiseService franchiseService,
        IAnimeListActionService listActionService,
        ILocalizer? localizer = null)
    {
        _anime = cloneAnime;
        _originalAnime = originalAnime;
        _editor = editor;
        _metadata = metadata;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _shikiApiService = shikiApiService;
        _animeRepo = animeRepo;
        _malApiService = malApiService;
        _franchiseService = franchiseService;
        _listActionService = listActionService;
        _localizer = localizer ?? Kiriha.Services.Metadata.LocalizationService.Active;

        BuildCustomShareLinks();

        _animePropertyChanged = (s, e) =>
        {
            if (e.PropertyName == nameof(AnimeEntity.Title) || e.PropertyName == nameof(AnimeEntity.RussianTitle))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
            if (e.PropertyName == nameof(AnimeEntity.Status) || e.PropertyName == nameof(AnimeEntity.Progress) || e.PropertyName == nameof(AnimeEntity.Score))
            {
                var current = FranchiseTimeline.FirstOrDefault(n => n.IsCurrent);
                if (current != null)
                {
                    current.UserStatus = _anime.Status;
                    current.UserProgress = _anime.Progress;
                    current.Score = _anime.Score;
                    UpdateFranchiseProgressStats();
                }
            }
        };
        _anime.PropertyChanged += _animePropertyChanged;

        InitializationAsync().SafeFireAndForget("AnimeDetailsInitialization");
    }

    public async Task NavigateToAnimeAsync(AnimeEntity target, bool addToHistory = true)
    {
        if (target == null) return;
        if (target.Id == Anime.Id && target.MediaKind == Anime.MediaKind) return;

        IsPosterPreviewOpen = false;

        if (!await TryLeaveEditorAsync()) return;

        if (addToHistory)
        {
            _navigationHistory.Push(_originalAnime);
            CanGoBack = _navigationHistory.Count > 0;
        }

        await OpenEditorAsync(target);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBack()
    {
        if (_navigationHistory.Count == 0) return;

        IsPosterPreviewOpen = false;

        if (!await TryLeaveEditorAsync()) return;

        var prev = _navigationHistory.Pop();
        CanGoBack = _navigationHistory.Count > 0;
        await OpenEditorAsync(prev);
    }

    private async Task<bool> TryLeaveEditorAsync()
    {
        if (_disposed || Editor.IsBusy) return false;
        if (!Editor.HasChanges) return true;

        var editor = Editor;
        if (!await editor.TrySaveAsync()) return false;
        HasAnySavedChanges = true;
        return !_disposed && ReferenceEquals(Editor, editor) && !editor.HasChanges;
    }

    private async Task OpenEditorAsync(AnimeEntity item)
    {
        Anime.PropertyChanged -= _animePropertyChanged;
        Editor.Dispose();

        _originalAnime = AnimeEditViewModel.ResolveEntry(_animeRepo, item);
        Anime = _originalAnime.Clone();

        Editor = new AnimeEditViewModel(_originalAnime, Anime, _listActionService, _animeRepo);
        Metadata = new AnimeMetadataViewModel(Anime, _malApiService);

        Anime.PropertyChanged += _animePropertyChanged;

        BuildCustomShareLinks();

        Relations.Clear();
        FranchiseTimeline.Clear();
        HasFranchiseTimeline = false;
        HasStandardRelations = false;
        HasAnyRelationsOrTimeline = false;

        await InitializationAsync();
    }

    partial void OnCanGoBackChanged(bool value)
    {
        GoBackCommand.NotifyCanExecuteChanged();
    }

    private void BuildCustomShareLinks()
    {
        CustomShareLinks.Clear();
        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = CustomLinkResolver.Resolve(link.UrlTemplate, Anime);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }

    private async Task InitializationAsync()
    {
        var editor = Editor;
        var metadata = Metadata;
        var original = _originalAnime;
        var generation = editor.EntryGeneration;
        bool IsCurrentEditor() => !_disposed && ReferenceEquals(editor, Editor);
        bool MatchesTitle(AnimeEntity? details) => details != null && details.Id == original.Id &&
            (details.MediaKind == MediaKind.Anime) == (original.MediaKind == MediaKind.Anime);

        async Task ApplyLoadedDetailsAsync(Task<AnimeEntity?> load)
        {
            var loaded = await load;
            if (!IsCurrentEditor() || loaded is null || !MatchesTitle(loaded)) return;

            metadata.ApplyFullDetails(loaded);
            var imported = await _animeRepo.RefreshEditorDetailsAsync(original, loaded, generation);
            if (IsCurrentEditor() && imported is { } entry)
                editor.ApplyImportedFields(entry.Fields, entry.Generation);
        }

        Anime.RefreshMetadata();
        metadata.NotifyMetadataChanged();

        IsLoading = Anime.Genres.Count == 0 || string.IsNullOrEmpty(Anime.Synopsis) ||
            (Anime.Presentation.IsAnime && Anime.Studios.Count == 0);
        try
        {
            var cached = await metadata.ReadCachedDetailsAsync();
            if (!IsCurrentEditor()) return;

            if (cached.Details is { } snapshot && MatchesTitle(snapshot))
            {
                if (IsLoading) metadata.ApplyFullDetails(snapshot);
                if (!cached.IsFresh)
                    ApplyLoadedDetailsAsync(metadata.RefreshCachedDetailsAsync()).SafeFireAndForget("RefreshCachedTitleDetails");
                return;
            }

            var load = ApplyLoadedDetailsAsync(metadata.FetchFullDetailsAsync());
            if (IsLoading)
                await load;
            else
                load.SafeFireAndForget("LoadTitleDetails");
        }
        finally
        {
            if (IsCurrentEditor())
            {
                IsLoading = false;
                _ = LoadFranchiseAndRelationsAsync();
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Anime.PropertyChanged -= _animePropertyChanged;
        Editor.Dispose();
        Relations.Clear();
        FranchiseTimeline.Clear();
        CustomShareLinks.Clear();
        _navigationHistory.Clear();
    }
}



