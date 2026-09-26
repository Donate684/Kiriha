using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Abstractions.Repositories;

public interface ISeasonalHiddenRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    IReadOnlySet<int> GetHiddenIds();
    bool IsHidden(int animeId);
    Task AddAsync(int animeId, CancellationToken ct = default);
    Task RemoveAsync(int animeId, CancellationToken ct = default);
    Task RemoveRangeAsync(IEnumerable<int> animeIds, CancellationToken ct = default);
}
