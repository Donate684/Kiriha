using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Dialogs;
using Kiriha.Infrastructure.Platform;
using Kiriha.Core.Shared.Shiki;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;
using Kiriha.Utils.Async;
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

    public System.Collections.ObjectModel.ObservableCollection<StaffPlusItemVm> StaffPlus { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly JikanApiService _jikanApiService;
    private readonly IDialogService _dialogs;
    private readonly Kiriha.Core.Abstractions.Services.IShikiApiService _shikiApiService;
    private readonly Kiriha.Core.Abstractions.Repositories.IAnimeRepository _animeRepo;
    private readonly Kiriha.Core.Abstractions.Services.IMalApiService _malApiService;

    public Kiriha.Core.Abstractions.Services.ISettingsService Settings => _settingsService;

    public AnimeDetailsViewModel(
        AnimeEntity cloneAnime,
        AnimeEditViewModel editor,
        AnimeMetadataViewModel metadata,
        JikanApiService jikanApiService,
        Kiriha.Core.Abstractions.Services.ISettingsService settingsService,
        IDialogService dialogs,
        Kiriha.Core.Abstractions.Services.IShikiApiService shikiApiService,
        Kiriha.Core.Abstractions.Repositories.IAnimeRepository animeRepo,
        Kiriha.Core.Abstractions.Services.IMalApiService malApiService)
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

        try
        {
            var relations = await _jikanApiService.GetRelationsAsync(Anime.Id, Anime.MediaKind);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Relations.Clear();
                foreach (var r in relations)
                {
                    var vm = new RelationItemVm(r);
                    Relations.Add(vm);
                    _ = FetchRelationImageAsync(vm);
                }
            });
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch relations for {Id}", Anime.Id);
        }

        try
        {
            var staffList = await _jikanApiService.GetStaffAsync(Anime.Id, Anime.MediaKind);
            _ = ProcessStaffPlusAsync(staffList);
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch staff for {Id}", Anime.Id);
        }
    }








}
