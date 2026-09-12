using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class MetadataRepository : IMetadataRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MetadataRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ShikiMetadata?> GetAsync(int id, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Metadata.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task UpsertAsync(ShikiMetadata meta, CancellationToken ct = default)
    {
        meta.FetchedAt = DateTime.UtcNow;

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.Metadata.AsTracking().FirstOrDefaultAsync(m => m.Id == meta.Id, ct);
        if (existing is null)
            context.Metadata.Add(meta);
        else
            context.Entry(existing).CurrentValues.SetValues(meta);
        await context.SaveChangesAsync(ct);
    }

    public async Task<HashSet<int>> GetAllIdsAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var ids = await context.Metadata.Select(m => m.Id).ToListAsync(ct);
        return new HashSet<int>(ids);
    }
}
