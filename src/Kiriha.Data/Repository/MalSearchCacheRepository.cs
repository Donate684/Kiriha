using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class MalSearchCacheRepository : IMalSearchCacheRepository
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MalSearchCacheRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<MalSearchCache?> GetAsync(string queryNormalized)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return null;
        using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.MalSearchCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized);
        if (entry == null) return null;

        var ttl = entry.AnimeId == 0 ? NegativeTtl : PositiveTtl;
        if (DateTime.UtcNow - entry.CreatedAt > ttl) return null;

        return entry;
    }

    public async Task UpsertAsync(string queryNormalized, int animeId, float score)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return;
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.MalSearchCache
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized);
        var now = DateTime.UtcNow;
        if (existing == null)
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
        await context.SaveChangesAsync();
    }
}
