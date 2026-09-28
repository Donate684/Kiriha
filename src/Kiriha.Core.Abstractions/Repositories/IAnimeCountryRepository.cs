namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeCountryRepository
{
    Task<Dictionary<int, string>> GetBatchAsync(IEnumerable<int> malIds, CancellationToken ct = default);
    Task UpsertBatchAsync(IReadOnlyDictionary<int, string> countries, CancellationToken ct = default);
}
