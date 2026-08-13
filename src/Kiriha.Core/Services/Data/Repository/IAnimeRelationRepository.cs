using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Data.Repository;

public interface IAnimeRelationRepository
{
    Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations);
}
