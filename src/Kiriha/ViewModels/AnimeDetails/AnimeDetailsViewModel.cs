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
    private AnimeEntity _anime;

    public AnimeEditViewModel Editor { get; }
    public AnimeMetadataViewModel Metadata { get; }

    [ObservableProperty]
    private bool _isLoading;


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

    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly IShikiApiService _shikiApiService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApiService;
    private readonly IFranchiseService _franchiseService;

    public ISettingsService Settings => _settingsService;

    public AnimeDetailsViewModel(
        AnimeEntity cloneAnime,
        AnimeEditViewModel editor,
        AnimeMetadataViewModel metadata,
        ISettingsService settingsService,
        IDialogService dialogs,
        IShikiApiService shikiApiService,
        IAnimeRepository animeRepo,
        IMalApiService malApiService,
        IFranchiseService franchiseService)
    {
        _anime = cloneAnime;
        Editor = editor;
        Metadata = metadata;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _shikiApiService = shikiApiService;
        _animeRepo = animeRepo;
        _malApiService = malApiService;
        _franchiseService = franchiseService;

        BuildCustomShareLinks();

        _animePropertyChanged = (s, e) =>
        {
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
    }
}



