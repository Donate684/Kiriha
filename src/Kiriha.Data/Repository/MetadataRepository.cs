using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Api;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class MetadataRepository : IMetadataRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MetadataRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ShikiMetadata?> GetAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Metadata.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task UpsertAsync(ShikiMetadata meta)
    {
        meta.FetchedAt = DateTime.UtcNow;

        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Metadata.AsTracking().FirstOrDefaultAsync(m => m.Id == meta.Id);
        if (existing == null)
            context.Metadata.Add(meta);
        else
            context.Entry(existing).CurrentValues.SetValues(meta);
        await context.SaveChangesAsync();
    }

    public async Task<HashSet<int>> GetAllIdsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var ids = await context.Metadata.Select(m => m.Id).ToListAsync();
        return new HashSet<int>(ids);
    }
}
