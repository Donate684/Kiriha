using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

public interface IAnimeStaffRepository
{
    Task<List<AnimeStaff>> GetBySourceIdAsync(int sourceMalId, CancellationToken ct = default);
    Task<DateTime?> GetFetchedAtAsync(int sourceMalId, CancellationToken ct = default);
    Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeStaff> staff, CancellationToken ct = default);
}
