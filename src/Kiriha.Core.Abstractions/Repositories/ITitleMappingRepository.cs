using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface ITitleMappingRepository
{
    Task<List<TitleMappingEntity>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(TitleMappingEntity entry, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<TitleMappingEntity> entries, CancellationToken ct = default);
    Task<bool> DeleteAsync(string normalizedTitle, CancellationToken ct = default);
}
