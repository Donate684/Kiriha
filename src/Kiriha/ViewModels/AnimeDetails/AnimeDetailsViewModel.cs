using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Utils.Async;
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
    private readonly Stack<(AnimeEntity Original, AnimeEntity Clone)> _navigationHistory = new();

    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly IShikiApiService _shikiApiService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApiService;
    private readonly IFranchiseService _franchiseService;

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
        IAnimeListActionService listActionService)
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

        if (Editor.HasChanges)
        {
            try
            {
                await _listActionService.SaveAnimeAsync(_originalAnime, Anime);
                HasAnySavedChanges = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to auto-save anime {Id} before navigating", Anime.Id);
            }
        }

        if (addToHistory)
        {
            _navigationHistory.Push((_originalAnime, Anime));
            CanGoBack = _navigationHistory.Count > 0;
        }

        Anime.PropertyChanged -= _animePropertyChanged;
        Editor.Dispose();

        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == target.Id && x.MediaKind == target.MediaKind);
        _originalAnime = existing ?? target;
        Anime = _originalAnime.Clone();

        Editor = new AnimeEditViewModel(_originalAnime, Anime, _listActionService);
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

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBack()
    {
        if (_navigationHistory.Count == 0) return;

        IsPosterPreviewOpen = false;

        if (Editor.HasChanges)
        {
            try
            {
                await _listActionService.SaveAnimeAsync(_originalAnime, Anime);
                HasAnySavedChanges = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to auto-save anime {Id} before going back", Anime.Id);
            }
        }

        var prev = _navigationHistory.Pop();
        CanGoBack = _navigationHistory.Count > 0;

        Anime.PropertyChanged -= _animePropertyChanged;
        Editor.Dispose();

        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == prev.Original.Id && x.MediaKind == prev.Original.MediaKind);
        _originalAnime = existing ?? prev.Original;
        Anime = _originalAnime.Clone();

        Editor = new AnimeEditViewModel(_originalAnime, Anime, _listActionService);
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
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, Anime);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }

    private async Task InitializationAsync()
    {
        Anime.RefreshMetadata();
        Metadata.NotifyMetadataChanged();

        if (Anime.Genres.Count == 0 || string.IsNullOrEmpty(Anime.Synopsis))
        {
            IsLoading = true;
            try
            {
                await Metadata.LoadFullDetailsAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        _ = LoadFranchiseAndRelationsAsync();
    }

    public void Dispose()
    {
        Anime.PropertyChanged -= _animePropertyChanged;
        Editor.Dispose();
        Relations.Clear();
        FranchiseTimeline.Clear();
        CustomShareLinks.Clear();
        _navigationHistory.Clear();
    }
}



