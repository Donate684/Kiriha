using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.Core.Repositories;

public interface IAnimeRelationRepository
{
    Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations);
}
