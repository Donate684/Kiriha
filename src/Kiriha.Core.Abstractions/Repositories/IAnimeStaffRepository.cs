using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Repositories;

public interface IAnimeStaffRepository
{
    Task<List<AnimeStaff>> GetBySourceIdAsync(int sourceMalId);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeStaff> staff);
}
