using System;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class MalSearchCacheRepository : IMalSearchCacheRepository
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TimeProvider _clock;

    public MalSearchCacheRepository(IDbContextFactory<AppDbContext> contextFactory, TimeProvider? clock = null)
    {
        _contextFactory = contextFactory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<MalSearchCache?> GetAsync(string queryNormalized, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return null;
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entry = await context.MalSearchCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized, ct);
        if (entry is null) return null;

        var ttl = entry.AnimeId == 0 ? NegativeTtl : PositiveTtl;
        if (_clock.GetUtcNow().UtcDateTime - entry.CreatedAt > ttl) return null;

        return entry;
    }

    public async Task UpsertAsync(string queryNormalized, int animeId, float score, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return;
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.MalSearchCache.AsTracking()
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            context.MalSearchCache.Add(new MalSearchCache
            {
                QueryNormalized = queryNormalized,
                AnimeId = animeId,
                Score = score,
                CreatedAt = now
            });
        }
        else
        {
            existing.AnimeId = animeId;
            existing.Score = score;
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync(ct);
    }
}
