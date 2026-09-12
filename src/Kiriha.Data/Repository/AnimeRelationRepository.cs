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

public sealed class AnimeRelationRepository : IAnimeRelationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnimeRelationRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeRelation>> GetBySourceIdAsync(int sourceMalId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Set<AnimeRelation>().AsNoTracking()
            .Where(x => x.SourceMalId == sourceMalId)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetFetchedAtAsync(int sourceMalId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var meta = await context.Set<AnimeRelationMeta>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == sourceMalId, ct);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int sourceMalId, IEnumerable<AnimeRelation> relations, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.Set<AnimeRelation>().Where(x => x.SourceMalId == sourceMalId).ToListAsync(ct);
        context.Set<AnimeRelation>().RemoveRange(existing);
        await context.Set<AnimeRelation>().AddRangeAsync(relations, ct);

        var meta = await context.Set<AnimeRelationMeta>().AsTracking().FirstOrDefaultAsync(m => m.MalId == sourceMalId, ct);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.Set<AnimeRelationMeta>().Add(new AnimeRelationMeta { MalId = sourceMalId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync(ct);
    }
}
