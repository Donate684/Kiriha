using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeRelationRepository
{
    Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations);
}
