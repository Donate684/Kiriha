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

    bool IsRecentlyDeleted(int animeId);
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
