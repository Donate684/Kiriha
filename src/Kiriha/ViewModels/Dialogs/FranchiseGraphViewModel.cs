using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Localization;
using Kiriha.Utils.Graphs;
using Serilog;

namespace Kiriha.ViewModels.Dialogs;

public partial class FranchiseGraphViewModel : ViewModelBase
{
    private readonly IShikiApiService _shikiApi;
    private readonly IMalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly IAnimeRepository _animeRepo;
    private readonly int _baseAnimeId;
    private ShikiFranchiseResponse? _rawFranchiseData;

    [ObservableProperty]
    private FranchiseGraphLayout? _layout;

    [ObservableProperty]
    private List<FranchiseGraphVisualNode> _timelineNodes = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _selectedViewMode; // 0 = Graph, 1 = Timeline

    [ObservableProperty]
    private bool _hideSpecials = true;

    [ObservableProperty]
    private bool _animeOnly = true;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private string _zoomText = "100%";

    [ObservableProperty]
    private string _franchiseTitle = string.Empty;

    public event Action? RequestCenterGraph;
    public event Action<double>? RequestZoomDelta;
    public event Action? RequestResetZoom;

    public FranchiseGraphViewModel(
        int animeId,
        IShikiApiService shikiApi,
        IMalApiService malApi,
        IDialogService dialogs,
        IAnimeRepository animeRepo)
    {
        _baseAnimeId = animeId;
        _shikiApi = shikiApi;
        _malApi = malApi;
        _dialogs = dialogs;
        _animeRepo = animeRepo;
    }

    partial void OnHideSpecialsChanged(bool value) => RecomputeLayout();

    partial void OnAnimeOnlyChanged(bool value) => RecomputeLayout();

    public void UpdateZoomDisplay(double scale)
    {
        ZoomLevel = scale;
        ZoomText = $"{Math.Round(scale * 100)}%";
    }

    [RelayCommand]
    private void ZoomIn()
    {
        RequestZoomDelta?.Invoke(1.2);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        RequestZoomDelta?.Invoke(1.0 / 1.2);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        RequestResetZoom?.Invoke();
    }

    [RelayCommand]
    private void FitToScreen()
    {
        RequestCenterGraph?.Invoke();
    }

    [RelayCommand]
    private void SelectGraphMode()
    {
        SelectedViewMode = 0;
    }

    [RelayCommand]
    private void SelectTimelineMode()
    {
        SelectedViewMode = 1;
    }

    public async Task LoadGraphAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var data = await _shikiApi.GetFranchiseAsync(_baseAnimeId);
            if (data != null && data.Nodes.Count > 0)
            {
                if (data.CurrentId == 0) data.CurrentId = _baseAnimeId;
                _rawFranchiseData = data;

                // Set franchise title
                var currentEntity = _animeRepo.Collection.FirstOrDefault(x => x.Id == _baseAnimeId && x.MediaKind == MediaKind.Anime);
                if (currentEntity != null)
                {
                    FranchiseTitle = !string.IsNullOrEmpty(currentEntity.RussianTitle)
                        ? currentEntity.RussianTitle
                        : currentEntity.Title;
                }
                else
                {
                    var currentNode = data.Nodes.FirstOrDefault(n => n.Id == data.CurrentId);
                    if (currentNode != null)
                    {
                        FranchiseTitle = currentNode.Name;
                    }
                }

                RecomputeLayout();
            }
            else
            {
                ErrorMessage = LocalizationStore.Translate("common.errors.franchise_load_failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load franchise graph for {AnimeId}", _baseAnimeId);
            ErrorMessage = LocalizationStore.Translate("common.errors.franchise_error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RecomputeLayout()
    {
        if (_rawFranchiseData == null || _rawFranchiseData.Nodes.Count == 0) return;

        var options = new FranchiseLayoutOptions
        {
            HideSpecials = HideSpecials,
            AnimeOnly = AnimeOnly
        };

        var layout = FranchiseLayoutEngine.CalculateLayout(_rawFranchiseData, options);

        foreach (var node in layout.Nodes)
        {
            EnrichNodeWithLocalData(node);
            _ = FetchNodeImageAsync(node);
        }

        Layout = layout;
        TimelineNodes = layout.TimelineNodes;
        RequestCenterGraph?.Invoke();
    }

    private void EnrichNodeWithLocalData(FranchiseGraphVisualNode node)
    {
        bool isManga = node.IsMangaOrNovel;
        var existing = _animeRepo.Collection.FirstOrDefault(x =>
            x.Id == node.Node.Id &&
            (isManga ? x.MediaKind != MediaKind.Anime : x.MediaKind == MediaKind.Anime));

        if (existing != null)
        {
            node.UserStatus = existing.Status;
            node.UserProgress = existing.Progress;
            node.TotalEpisodes = existing.TotalEpisodes;
            node.Score = existing.Score;
            node.RussianTitle = existing.RussianTitle;

            if (!string.IsNullOrEmpty(existing.MainPictureUrl))
            {
                node.DisplayImageUrl = existing.MainPictureUrl;
            }
        }
    }

    private async Task FetchNodeImageAsync(FranchiseGraphVisualNode node)
    {
        if (!string.IsNullOrEmpty(node.DisplayImageUrl)) return;

        bool isManga = node.IsMangaOrNovel;
        try
        {
            AnimeEntity? details = isManga
                ? await _malApi.GetMangaDetailsAsync(node.Node.Id)
                : await _malApi.GetAnimeDetailsAsync(node.Node.Id);

            if (details != null && !string.IsNullOrEmpty(details.MainPictureUrl))
            {
                node.DisplayImageUrl = details.MainPictureUrl;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FranchiseGraph: failed to fetch MAL image for node {Id}", node.Node.Id);
        }
    }

    [RelayCommand]
    private async Task NodeClicked(FranchiseGraphVisualNode node)
    {
        if (node is null || node.Node is null) return;

        MediaKind kind = node.Node.Kind.ToLowerInvariant() switch
        {
            "manga" or "manhwa" or "manhua" or "one_shot" or "doujin" => MediaKind.Manga,
            "novel" or "light_novel" => MediaKind.LightNovel,
            _ => MediaKind.Anime
        };

        var targetAnime = new AnimeEntity
        {
            Id = node.Node.Id,
            Title = node.Node.Name,
            RussianTitle = node.RussianTitle,
            MediaKind = kind,
            MainPictureUrl = !string.IsNullOrEmpty(node.DisplayImageUrl) ? node.DisplayImageUrl : node.Node.ImageUrl,
            Status = node.UserStatus,
            Progress = node.UserProgress ?? 0,
            TotalEpisodes = node.TotalEpisodes ?? 0
        };

        await _dialogs.ShowAnimeDetailsAsync(null, targetAnime);
    }
}
