using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class TitleMappingRepository : ITitleMappingRepository
{
    private const int CommandTimeoutSeconds = 1;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public TitleMappingRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<TitleMappingEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        return await context.TitleMappings.AsNoTracking().ToListAsync(ct);
    }

    public async Task UpsertAsync(TitleMappingEntity entry, CancellationToken ct = default)
    {
        entry.UpdatedAt = DateTime.UtcNow;

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.Database.SetCommandTimeout(CommandTimeoutSeconds);
            var existing = await context.TitleMappings
                .AsTracking()
                .FirstOrDefaultAsync(x => x.NormalizedTitle == entry.NormalizedTitle, ct);
            if (existing is null)
                context.TitleMappings.Add(entry);
            else
                context.Entry(existing).CurrentValues.SetValues(entry);

            await context.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpsertBatchAsync(IEnumerable<TitleMappingEntity> entries, CancellationToken ct = default)
    {
        var list = entries.ToList();
        if (list.Count == 0)
            return;

        var now = DateTime.UtcNow;
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.Database.SetCommandTimeout(CommandTimeoutSeconds);
            var existingMap = await context.TitleMappings
                .AsTracking()
                .ToDictionaryAsync(x => x.NormalizedTitle, StringComparer.Ordinal, ct);

            foreach (var item in list)
            {
                item.UpdatedAt = now;
                if (existingMap.TryGetValue(item.NormalizedTitle, out var existing))
                    context.Entry(existing).CurrentValues.SetValues(item);
                else
                    context.TitleMappings.Add(item);
            }

            await context.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string normalizedTitle, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            context.Database.SetCommandTimeout(CommandTimeoutSeconds);
            var removed = await context.TitleMappings
                .Where(x => x.NormalizedTitle == normalizedTitle)
                .ExecuteDeleteAsync(ct);
            return removed > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
