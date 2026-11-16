namespace Kiriha.Core.Abstractions.Repositories;

public interface IAniListMediaMapRepository
{
    Task<int?> GetAniListIdAsync(int primaryId, string mediaType, CancellationToken ct = default);
    Task UpsertAsync(int primaryId, string mediaType, int aniListId, CancellationToken ct = default);
}
