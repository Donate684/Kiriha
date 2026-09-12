using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class AnimeStaffRepository : IAnimeStaffRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnimeStaffRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeStaff>> GetBySourceIdAsync(int sourceMalId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Set<AnimeStaff>().AsNoTracking()
            .Where(x => x.SourceMalId == sourceMalId)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetFetchedAtAsync(int sourceMalId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var meta = await context.Set<AnimeStaffMeta>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == sourceMalId, ct);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeStaff> staff, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.Set<AnimeStaff>().Where(x => x.SourceMalId == sourceMalId).ToListAsync(ct);
        context.Set<AnimeStaff>().RemoveRange(existing);
        await context.Set<AnimeStaff>().AddRangeAsync(staff, ct);

        var meta = await context.Set<AnimeStaffMeta>().AsTracking().FirstOrDefaultAsync(m => m.MalId == sourceMalId, ct);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.Set<AnimeStaffMeta>().Add(new AnimeStaffMeta { MalId = sourceMalId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync(ct);
    }
}
