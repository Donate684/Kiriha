using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeRepository
{
    Task InitializationTask { get; }
    bool IsInitializing { get; }
    Task InitializeAsync() => InitializationTask;

    IEnumerable<AnimeEntity> GetCollection();
    AnimeEntity? FindById(int animeId, MediaKind kind = MediaKind.Anime) =>
        Collection?.FirstOrDefault(x => x.Id == animeId && x.MediaKind == kind);
    long GetEntryGeneration(int animeId, MediaKind kind);
    Task<bool> MergeTrackedDetailsAsync(AnimeEntity details, MediaKey key, long generation, CancellationToken ct = default);
    Task<(AnimeEditFields Fields, long Generation)?> RefreshEditorDetailsAsync(
        AnimeEntity original, AnimeEntity details, long generation, CancellationToken ct = default);

    bool IsRecentlyDeleted(int animeId, MediaKind kind);
    Task AddOrUpdateAnimeAsync(AnimeEntity anime);
    Task<List<AnimeEntity>> GetSnapshotAsync(MediaKind[] kinds);
    Task<IReadOnlyList<AnimeEntity>> GetSnapshotAsync();
    IReadOnlyList<AnimeEntity>? TryGetSnapshot() => null;
    System.Collections.ObjectModel.ObservableCollection<AnimeEntity> Collection { get; }
    void AddToCollection(AnimeEntity entity);
    Task ApplySyncBatchAsync(List<AnimeEntity> toRemove, List<System.Action> uiBatch);
    Task RemoveAnimeLocalAsync(int animeId);
    Task RemoveAnimeLocalAsync(int animeId, MediaKind kind);
}

public static class AnimeRepositoryEntryExtensions
{
    public static LibraryEntryTarget? CaptureEntry(this IAnimeRepository repository, AnimeEntity? item)
    {
        if (item == null) return null;

        var key = new MediaKey(item.Id, item.MediaKind);
        var generation = repository.GetEntryGeneration(key.Id, key.Kind);
        return ReferenceEquals(repository.FindById(key.Id, key.Kind), item) && item.Status != UserAnimeStatus.None
            ? new LibraryEntryTarget(key, generation)
            : null;
    }
}
