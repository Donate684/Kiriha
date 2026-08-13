using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class EpisodeReleaseRepository : IEpisodeReleaseRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EpisodeReleaseRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<EpisodeRelease>> GetByMalIdAsync(int malId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpisodeReleases.AsNoTracking()
            .Where(x => x.MalId == malId)
            .ToListAsync();
    }

    public async Task<DateTime?> GetFetchedAtAsync(int malId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var meta = await context.EpisodeListMeta.AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == malId);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int malId, IEnumerable<EpisodeRelease> episodes)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.EpisodeReleases.Where(x => x.MalId == malId).ToListAsync();
        context.EpisodeReleases.RemoveRange(existing);
        await context.EpisodeReleases.AddRangeAsync(episodes);

        var meta = await context.EpisodeListMeta.FirstOrDefaultAsync(m => m.MalId == malId);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.EpisodeListMeta.Add(new EpisodeListMeta { MalId = malId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync();
    }
}
