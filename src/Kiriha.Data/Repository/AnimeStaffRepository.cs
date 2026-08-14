using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class AnimeStaffRepository : IAnimeStaffRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnimeStaffRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeStaff>> GetBySourceIdAsync(int sourceMalId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<AnimeStaff>().AsNoTracking()
            .Where(x => x.SourceMalId == sourceMalId)
            .ToListAsync();
    }

    public async Task<DateTime?> GetFetchedAtAsync(int sourceMalId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var meta = await context.Set<AnimeStaffMeta>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == sourceMalId);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeStaff> staff)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Set<AnimeStaff>().Where(x => x.SourceMalId == sourceMalId).ToListAsync();
        context.Set<AnimeStaff>().RemoveRange(existing);
        await context.Set<AnimeStaff>().AddRangeAsync(staff);

        var meta = await context.Set<AnimeStaffMeta>().AsTracking().FirstOrDefaultAsync(m => m.MalId == sourceMalId);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.Set<AnimeStaffMeta>().Add(new AnimeStaffMeta { MalId = sourceMalId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync();
    }
}
