using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Infrastructure.Platform;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using Kiriha.Utils.Async;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Startup;
using Serilog;

namespace Kiriha.ViewModels.AnimeDetails;


public partial class AnimeDetailsViewModel : ViewModelBase
{
    [ObservableProperty]
    private AnimeEntity _anime;

    public AnimeEditViewModel Editor { get; }
    public AnimeMetadataViewModel Metadata { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    public System.Collections.ObjectModel.ObservableCollection<AnimeOfflineItem> _relatedAnime = new();

    public System.Collections.ObjectModel.ObservableCollection<RelationItemVm> Relations { get; } = new();

    [ObservableProperty]
    private bool _hasFranchiseTimeline;

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
    private readonly JikanApiService _jikanApiService;
    private readonly IDialogService _dialogs;
    private readonly IShikiApiService _shikiApiService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApiService;

    public ISettingsService Settings => _settingsService;

    public AnimeDetailsViewModel(
        AnimeEntity cloneAnime,
        AnimeEditViewModel editor,
        AnimeMetadataViewModel metadata,
        JikanApiService jikanApiService,
        ISettingsService settingsService,
        IDialogService dialogs,
        IShikiApiService shikiApiService,
        IAnimeRepository animeRepo,
        IMalApiService malApiService)
    {
        _anime = cloneAnime;
        Editor = editor;
        Metadata = metadata;
        _jikanApiService = jikanApiService;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _shikiApiService = shikiApiService;
        _animeRepo = animeRepo;
        _malApiService = malApiService;

        BuildCustomShareLinks();

        _anime.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AnimeEntity.Status) || e.PropertyName == nameof(AnimeEntity.Progress))
            {
                var current = FranchiseTimeline.FirstOrDefault(n => n.IsCurrent);
                if (current != null)
                {
                    current.UserStatus = _anime.Status;
                    current.UserProgress = _anime.Progress;
                    UpdateFranchiseProgressStats();
                }
            }
        };

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








}



