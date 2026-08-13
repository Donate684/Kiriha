using System;
using System.Threading.Tasks;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public class AnimeProgressService : Kiriha.Core.Services.IProgressUpdateService
{
    private readonly Kiriha.Core.Repositories.IAnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly Kiriha.Core.Services.ISyncManager _syncManager;
    private readonly Kiriha.Core.Services.IHistoryService _historyService;
    private readonly Kiriha.Core.Infrastructure.IUiDispatcher _uiDispatcher;

    public AnimeProgressService(
        Kiriha.Core.Repositories.IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        Kiriha.Core.Services.ISyncManager syncManager,
        Kiriha.Core.Services.IHistoryService historyService,
        Kiriha.Core.Infrastructure.IUiDispatcher uiDispatcher)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
    }

    public async Task RemoveAnimeAsync(int animeId)
    {
        // Remove locally first so the UI is responsive even when offline.
        await _animeRepository.RemoveAnimeLocalAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeProgressService: Failed to enqueue Remove sync task for {AnimeId}", animeId);
        }
    }

    public virtual async Task<bool> UpdateProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) && item.StatusDetailed == "Not yet aired")
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);

        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Progress = nextProgress;
            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                item.Status = nextStatus.Value;
        });

        return true;
    }

    public virtual async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;

        bool isManga = item.MediaKind != MediaKind.Anime;

        // Manga completion
        if (isManga && item.Chapters > 0 && nextProgress >= item.Chapters && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;
        // Anime completion
        else if (!isManga && item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;

        if (isManga)
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                item.ChaptersRead = nextProgress;
                if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                    item.Status = nextStatus.Value;
            });

            await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
            await _syncManager.EnqueueFullUpdateAsync(item);

            _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Read");
            return nextStatus;
        }
        else
        {
            if (await UpdateProgressAsync(item, nextProgress, nextStatus))
            {
                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Watched");
                return nextStatus;
            }
        }

        return null;
    }

    public async Task SmartDecrementProgressAsync(AnimeEntity item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        if (isManga)
        {
            if (item.ChaptersRead > 0)
            {
                int nextProgress = item.ChaptersRead - 1;
                await _uiDispatcher.InvokeAsync(() =>
                {
                    item.ChaptersRead = nextProgress;
                });

                await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, null);
                await _syncManager.EnqueueFullUpdateAsync(item);

                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
            }
        }
        else
        {
            if (item.Progress > 0)
            {
                int nextProgress = item.Progress - 1;
                if (await UpdateProgressAsync(item, nextProgress))
                {
                    _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
                }
            }
        }
    }

    public async Task SetScoreAsync(AnimeEntity item, int score)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Score = score.ToString();
        });
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);
        await _syncManager.EnqueueUpdateAsync(item.Id, item.Progress, score: score);
        _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, item.Progress, "ScoreSet", score.ToString());
    }
}
