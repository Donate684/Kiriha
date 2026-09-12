using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeRelationRepository
{
    Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId, CancellationToken ct = default);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId, CancellationToken ct = default);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations, CancellationToken ct = default);
}
