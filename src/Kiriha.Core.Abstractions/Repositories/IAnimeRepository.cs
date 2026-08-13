using System.Collections.Generic;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeRepository
{
    Task InitializationTask { get; }
    bool IsInitializing { get; }
    
    // For now we expose IEnumerable<AnimeEntity>. The concrete UI repo will have to map it or we change Tracking to not iterate Collection directly.
    IEnumerable<AnimeEntity> GetCollection();
    
    bool IsRecentlyDeleted(int animeId);
    Task AddOrUpdateAnimeAsync(AnimeEntity anime);
    Task<List<AnimeEntity>> GetSnapshotAsync(MediaKind[] kinds);
    System.Collections.ObjectModel.ObservableCollection<AnimeEntity> Collection { get; }
    void AddToCollection(AnimeEntity entity);
    Task ApplySyncBatchAsync(List<AnimeEntity> toRemove, List<System.Action> uiBatch);
    Task RemoveAnimeLocalAsync(int animeId);
}
