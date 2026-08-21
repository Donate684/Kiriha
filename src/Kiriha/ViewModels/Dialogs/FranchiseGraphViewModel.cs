using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Dialogs;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Utils.Graphs;
using System.Linq;
using Serilog;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.ViewModels.Dialogs;

public partial class FranchiseGraphViewModel : ViewModelBase
{
    private readonly IShikiApiService _shikiApi;
    private readonly IMalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly IAnimeRepository _animeRepo;
    private readonly int _baseAnimeId;

    [ObservableProperty]
    private FranchiseGraphLayout? _layout;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public FranchiseGraphViewModel(int animeId, IShikiApiService shikiApi, IMalApiService malApi, IDialogService dialogs, IAnimeRepository animeRepo)
    {
        _baseAnimeId = animeId;
        _shikiApi = shikiApi;
        _malApi = malApi;
        _dialogs = dialogs;
        _animeRepo = animeRepo;
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
                // Assign currentId if the API doesn't do it reliably
                if (data.CurrentId == 0) data.CurrentId = _baseAnimeId;

                Layout = FranchiseLayoutEngine.CalculateLayout(data);

                foreach (var node in Layout.Nodes)
                {
                    _ = FetchNodeImageAsync(node);
                }
            }
            else
            {
                ErrorMessage = Kiriha.Localization.LocalizationStore.Translate("common.errors.franchise_load_failed");
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Failed to load franchise graph for {AnimeId}", _baseAnimeId);
            ErrorMessage = Kiriha.Localization.LocalizationStore.Translate("common.errors.franchise_error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task FetchNodeImageAsync(FranchiseGraphVisualNode node)
    {
        bool isManga = node.Node.Kind == "manga" || node.Node.Kind == "manhwa" || node.Node.Kind == "manhua" ||
                       node.Node.Kind == "one_shot" || node.Node.Kind == "doujin" || node.Node.Kind == "novel" ||
                       node.Node.Kind == "light_novel";

        // 1. Проверяем локальный репозиторий
        var existing = _animeRepo.Collection.FirstOrDefault(x =>
            x.Id == node.Node.Id &&
            (isManga ? x.MediaKind != MediaKind.Anime : x.MediaKind == MediaKind.Anime));

        if (existing != null)
        {
            node.UserStatus = existing.Status;
            
            if (!string.IsNullOrEmpty(existing.MainPictureUrl))
            {
                node.DisplayImageUrl = existing.MainPictureUrl;
                return;
            }
        }

        // 2. Запрашиваем с MAL API по ID
        try
        {
            AnimeEntity? details = isManga
                ? await _malApi.GetMangaDetailsAsync(node.Node.Id)
                : await _malApi.GetAnimeDetailsAsync(node.Node.Id);

            if (details != null && !string.IsNullOrEmpty(details.MainPictureUrl))
                node.DisplayImageUrl = details.MainPictureUrl;
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "FranchiseGraph: failed to fetch MAL image for node {Id}", node.Node.Id);
        }
    }

    [RelayCommand]
    private async Task NodeClicked(FranchiseGraphVisualNode node)
    {
        if (node == null || node.Node == null) return;

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
            MediaKind = kind,
            MainPictureUrl = node.Node.ImageUrl
        };

        await _dialogs.ShowAnimeDetailsAsync(null, targetAnime);
    }
}
