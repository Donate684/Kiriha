using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeProgressService
{
    private async Task<(AnimeEntity Item, AnimeEntity Stored)?> FindCurrentEntryAsync(
        LibraryEntryTarget target, CancellationToken ct)
    {
        var (id, kind) = target.Key;
        if (_animeRepository.GetEntryGeneration(id, kind) != target.Generation ||
            _animeRepository.FindById(id, kind) is not { Status: not UserAnimeStatus.None } current)
            return null;

        var stored = await _userAnimeRepo.GetByIdAsync(id, kind, ct);
        return stored is { Status: not UserAnimeStatus.None } ? (current, stored) : null;
    }

    public Task<bool> ChangeProgressAsync(LibraryEntryTarget target, int delta) =>
        SerializeProgressChangeAsync(async () =>
        {
            if (delta is not (-1 or 1) ||
                await FindCurrentEntryAsync(target, CancellationToken.None) is not { } entry)
                return false;

            var (item, stored) = entry;
            int progress = ReadProgress(stored);
            int total = item.MediaKind == MediaKind.Anime ? item.TotalEpisodes : item.Chapters;
            if (ReadProgress(item) != progress) return false;

            if (delta < 0)
                return progress > 0 && await SaveProgressChangeAsync(
                    item, progress - 1, null, HistoryActionType.Reverted, onlyAdvance: false, CancellationToken.None);

            if (progress == int.MaxValue || (total > 0 && progress >= total)) return false;
            return (await SaveWatchedAsync(item, progress + 1, onlyAdvance: true)).Saved;
        }, CancellationToken.None);

    private Task<UserAnimeStatus?> SetObservedProgressAsync(LibraryEntryTarget target, int observedProgress, int nextProgress) =>
        SerializeProgressChangeAsync(async () =>
        {
            if (nextProgress < 0 || nextProgress == observedProgress ||
                await FindCurrentEntryAsync(target, CancellationToken.None) is not { } entry ||
                ReadProgress(entry.Item) != observedProgress || ReadProgress(entry.Stored) != observedProgress)
                return null;

            return (await SaveWatchedAsync(entry.Item, nextProgress, onlyAdvance: false)).Status;
        }, CancellationToken.None);

    public Task<bool> SetScoreAsync(LibraryEntryTarget target, string expectedScore, int score) =>
        SaveScoreAsync(target, expectedScore, score);

    private async Task<bool> SaveScoreAsync(LibraryEntryTarget target, string? expectedScore, int score)
    {
        if (_entryWriter is not { } writer || score is < 0 or > 10) return false;
        using var write = await writer.AcquireWriteAsync(CancellationToken.None);
        if (await FindCurrentEntryAsync(target, CancellationToken.None) is not { } entry) return false;

        var (item, stored) = entry;
        var draft = item.Clone();
        draft.UserScore = score > 0 ? score : null;
        if (draft.Score == stored.Score ||
            (expectedScore != null && (stored.Score != expectedScore || item.Score != expectedScore)))
            return false;

        await writer.SaveAsync(draft, [writer.CreateHistory(draft, HistoryActionType.ScoreSet, score.ToString())],
            CancellationToken.None, item);
        return true;
    }

    public async Task<bool> ConfirmRewatchAsync(LibraryEntryTarget target, int expectedRewatchCount, int episode)
    {
        if (_entryWriter is not { } writer || target.Key.Kind != MediaKind.Anime || episode <= 0) return false;
        using var write = await writer.AcquireWriteAsync(CancellationToken.None);
        if (await FindCurrentEntryAsync(target, CancellationToken.None) is not { } entry ||
            !CanStartRewatch(entry.Item, expectedRewatchCount) || !CanStartRewatch(entry.Stored, expectedRewatchCount))
            return false;

        var item = entry.Item;
        if (item.TotalEpisodes > 0 && episode > item.TotalEpisodes) return false;

        var draft = item.Clone();
        ApplyRewatch(draft, episode);
        await writer.SaveAsync(draft, [writer.CreateHistory(draft, HistoryActionType.Rewatching)], CancellationToken.None, item);
        await _uiDispatcher.InvokeAsync(() => _eventBus.Publish(new AnimeProgressChangedMessage(item, episode)));
        return true;
    }

    private static bool CanStartRewatch(AnimeEntity item, int expectedRewatchCount) =>
        item is { Status: UserAnimeStatus.Completed, IsRewatching: false, RewatchCount: < int.MaxValue } &&
        item.RewatchCount == expectedRewatchCount;

    private void ApplyRewatch(AnimeEntity entry, int episode)
    {
        entry.IsRewatching = true;
        entry.RewatchCount++;
        entry.Progress = episode;
        entry.Status = UserAnimeStatus.Watching;
        entry.DateStarted = _clock.GetLocalNow().Date;
        entry.DateCompleted = null;
    }

    private static int ReadProgress(AnimeEntity item) => item.MediaKind == MediaKind.Anime ? item.Progress : item.ChaptersRead;
}
