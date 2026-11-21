using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public enum ScrobbleOutcome { Skipped, EntryChanged, AlreadyWatched, Saved }

public readonly record struct ScrobbleResult(ScrobbleOutcome Outcome, AnimeEntity? Entry = null, UserAnimeStatus? NextStatus = null);

public interface IProgressUpdateService
{
    Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null);
    Task<bool> RecordScrobbleAsync(AnimeEntity anime, int episode, UserAnimeStatus? nextStatus, CancellationToken ct);
    Task<ScrobbleResult> RecordScrobbleAsync(MediaKey key, long generation, int episode, CancellationToken ct);
    Task RemoveAnimeAsync(int animeId);
    Task RemoveAnimeAsync(int animeId, MediaKind mediaKind);
    Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress);
    Task<bool> MarkEpisodeWatchedAsync(MediaKey key, long generation, int episode);
    Task SmartDecrementProgressAsync(AnimeEntity item);
    Task<bool> ChangeProgressAsync(LibraryEntryTarget target, int delta);
    Task SetScoreAsync(AnimeEntity item, int score);
    Task<bool> SetScoreAsync(LibraryEntryTarget target, string expectedScore, int score);
    Task ConfirmRewatchAsync(AnimeEntity item, int episode = 1);
    Task<bool> ConfirmRewatchAsync(LibraryEntryTarget target, int expectedRewatchCount, int episode);
}
