using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class MetadataRepository : IMetadataRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MetadataRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ShikiMetadata?> GetAsync(int id, MediaKind mediaKind = MediaKind.Anime, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Metadata
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.MediaKind == mediaKind, ct);
    }

    public async Task UpsertAsync(ShikiMetadata meta, CancellationToken ct = default)
    {
        meta.FetchedAt = DateTime.UtcNow;

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.Metadata
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == meta.Id && m.MediaKind == meta.MediaKind, ct);
        if (existing is null)
            context.Metadata.Add(meta);
        else
            context.Entry(existing).CurrentValues.SetValues(meta);
        await context.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<MediaKey, ShikiMetadata>> GetBatchAsync(IEnumerable<MediaKey> keys, CancellationToken ct = default)
    {
        var keySet = keys.ToHashSet();
        if (keySet.Count == 0)
            return new Dictionary<MediaKey, ShikiMetadata>();

        var idList = keySet.Select(k => k.Id).Distinct().ToList();

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var rows = await context.Metadata
            .AsNoTracking()
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(ct);

        var dict = new Dictionary<MediaKey, ShikiMetadata>(keySet.Count);
        foreach (var row in rows)
        {
            var key = new MediaKey(row.Id, row.MediaKind);
            if (keySet.Contains(key))
            {
                dict[key] = row;
            }
        }

        return dict;
    }
}
