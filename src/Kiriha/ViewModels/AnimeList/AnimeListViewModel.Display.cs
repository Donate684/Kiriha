using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncMalCommand))]
    private bool _isBusy;
    [ObservableProperty] private AnimeEntity? _activeItem;
    private (LibraryEntryTarget Target, string Score)? _scoreMenuEntry;

    public void EnqueueItemForViewport(AnimeEntity item)
    {
        if (item is null) return;
        _queueService.EnqueueForViewport([item]);
    }

    public void EnqueueItemsForViewport(IEnumerable<AnimeEntity> items)
    {
        _queueService.EnqueueForViewport(items);
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
    public Task IncrementProgress(AnimeEntity item) => ChangeLibraryProgressAsync(item, 1);

    public async Task SetProgressTo(AnimeEntity item, int nextProgress)
    {
        var oldStatus = item.Status;
        var oldRewatching = item.IsRewatching;

        try
        {
            await _progressService.SmartIncrementProgressAsync(item, nextProgress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set library progress for {AnimeId} to {Progress}", item.Id, nextProgress);
            return;
        }

        await UpdateCountsAsync();

        // Only re-filter if the status changed (item needs to move to another tab)
        if (item.Status != oldStatus || item.IsRewatching != oldRewatching)
        {
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public Task DecrementProgress(AnimeEntity item) => ChangeLibraryProgressAsync(item, -1);

    private async Task ChangeLibraryProgressAsync(AnimeEntity item, int delta)
    {
        if (_animeRepo.CaptureEntry(item) is not { } target) return;
        var previousStatus = item.Status;
        var wasRewatching = item.IsRewatching;
        try
        {
            if (!await _progressService.ChangeProgressAsync(target, delta)) return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to change library progress for {AnimeId}", item.Id);
            return;
        }
        await UpdateCountsAsync();
        if (item.Status != previousStatus || item.IsRewatching != wasRewatching)
            await ApplyCurrentFiltersAsync();
    }

    [RelayCommand]
    public void OpenScoreMenu(AnimeEntity? item)
    {
        ActiveItem = item;
        _scoreMenuEntry = item != null && _animeRepo.CaptureEntry(item) is { } target ? (target, item.Score) : null;
    }

    [RelayCommand]
    public async Task ApplyScoreFromMenu(RatingOption rating)
    {
        if (_scoreMenuEntry is not { } entry || rating is null) return;
        if (!int.TryParse(rating.Value, out int score) && rating.Value != "-") return;
        try
        {
            await _progressService.SetScoreAsync(entry.Target, entry.Score, score);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save the score for library entry {Id}", entry.Target.Key.Id);
        }
    }

    [RelayCommand]
    public async Task SetScore(AnimeEntity item)
    {
        if (item is null) return;
        await SetLibraryScoreAsync(item, item.UserScore ?? 0);
    }

    private async Task SetLibraryScoreAsync(AnimeEntity item, int score)
    {
        try
        {
            await _progressService.SetScoreAsync(item, score);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save the score for library entry {Id}", item.Id);
        }
    }
}
