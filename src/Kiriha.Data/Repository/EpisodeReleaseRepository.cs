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

public sealed class EpisodeReleaseRepository : IEpisodeReleaseRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EpisodeReleaseRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<EpisodeRelease>> GetByMalIdAsync(int malId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.EpisodeReleases.AsNoTracking()
            .Where(x => x.MalId == malId)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetFetchedAtAsync(int malId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var meta = await context.EpisodeListMeta.AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == malId, ct);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int malId, IEnumerable<EpisodeRelease> episodes, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.EpisodeReleases.Where(x => x.MalId == malId).ToListAsync(ct);
        context.EpisodeReleases.RemoveRange(existing);
        await context.EpisodeReleases.AddRangeAsync(episodes, ct);

        var meta = await context.EpisodeListMeta.AsTracking().FirstOrDefaultAsync(m => m.MalId == malId, ct);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.EpisodeListMeta.Add(new EpisodeListMeta { MalId = malId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync(ct);
    }
}
