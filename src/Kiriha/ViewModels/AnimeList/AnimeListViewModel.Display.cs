using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Utils.Async;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    [ObservableProperty] private AvaloniaList<AnimeEntity> _filteredItems = new();
    [ObservableProperty] private string _watchingHeader = string.Empty;
    [ObservableProperty] private string _completedHeader = string.Empty;
    [ObservableProperty] private string _onHoldHeader = string.Empty;
    [ObservableProperty] private string _droppedHeader = string.Empty;
    [ObservableProperty] private string _planToWatchHeader = string.Empty;

    [ObservableProperty] private AnimeEntity? _selectedItem;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AnimeEntity? _activeItem;

    public void EnqueueItemForViewport(AnimeEntity item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    public void RefreshAfterDetailsEdit()
    {
        RebuildListProjection();
        UpdateCountsAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
        ApplyCurrentFiltersAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
    }

    private void RebuildListProjection()
    {
        _listProjection.Rebuild(_animeRepo.Collection);
    }

    [RelayCommand]
    public async Task IncrementProgress(AnimeEntity item)
    {
        if (item.TotalEpisodes == 0 || item.Progress < item.TotalEpisodes)
        {
            await SetProgressTo(item, item.Progress + 1);
        }
    }

    public async Task SetProgressTo(AnimeEntity item, int nextProgress)
    {
        var oldStatus = item.Status;
        var oldRewatching = item.IsRewatching;

        await _progressService.SmartIncrementProgressAsync(item, nextProgress);

        await UpdateCountsAsync();

        // Only re-filter if the status changed (item needs to move to another tab)
        if (item.Status != oldStatus || item.IsRewatching != oldRewatching)
        {
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public async Task DecrementProgress(AnimeEntity item)
    {
        await _progressService.SmartDecrementProgressAsync(item);
        await UpdateCountsAsync();
    }

    [RelayCommand]
    public void OpenScoreMenu(AnimeEntity item) => ActiveItem = item;

    [RelayCommand]
    public async Task ApplyScoreFromMenu(RatingOption rating)
    {
        if (ActiveItem == null || rating == null) return;
        int.TryParse(rating.Value, out int score);
        await _progressService.SetScoreAsync(ActiveItem, score);
    }

    [RelayCommand]
    public async Task SetScore(AnimeEntity item)
    {
        if (item == null) return;
        int.TryParse(item.Score, out int score);
        await _progressService.SetScoreAsync(item, score);
    }
}
