using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class AnimeRelationRepository : IAnimeRelationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnimeRelationRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<AnimeRelation>().AsNoTracking()
            .Where(x => x.SourceMalId == sourceMalId)
            .ToListAsync();
    }

    public async Task<DateTime?> GetFetchedAtAsync(int sourceMalId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var meta = await context.Set<AnimeRelationMeta>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == sourceMalId);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Set<AnimeRelation>().Where(x => x.SourceMalId == sourceMalId).ToListAsync();
        context.Set<AnimeRelation>().RemoveRange(existing);
        await context.Set<AnimeRelation>().AddRangeAsync(relations);

        var meta = await context.Set<AnimeRelationMeta>().AsTracking().FirstOrDefaultAsync(m => m.MalId == sourceMalId);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.Set<AnimeRelationMeta>().Add(new AnimeRelationMeta { MalId = sourceMalId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync();
    }
}
