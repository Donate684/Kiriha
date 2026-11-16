using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class AniListMediaMapRepository : IAniListMediaMapRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AniListMediaMapRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int?> GetAniListIdAsync(int primaryId, string mediaType, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entry = await context.AniListMediaMap
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrimaryId == primaryId && x.MediaType == mediaType, ct);
        return entry?.AniListId;
    }

    public async Task UpsertAsync(int primaryId, string mediaType, int aniListId, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var existing = await context.AniListMediaMap
                .AsTracking()
                .FirstOrDefaultAsync(x => x.PrimaryId == primaryId && x.MediaType == mediaType, ct);
            if (existing is null)
            {
                context.AniListMediaMap.Add(new AniListMediaMapEntity
                {
                    PrimaryId = primaryId,
                    MediaType = mediaType,
                    AniListId = aniListId,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.AniListId = aniListId;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
