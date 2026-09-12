using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class SyncTaskRepository : ISyncTaskRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SyncTaskRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> AddAsync(SyncTaskEntity task, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SyncTasks.Add(task);
        await context.SaveChangesAsync(ct);
        return task.Id;
    }

    public async Task<List<SyncTaskEntity>> GetPendingAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.SyncTasks.OrderBy(t => t.Id).ToListAsync(ct);
    }

    public async Task UpdateAsync(SyncTaskEntity task, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SyncTasks.Update(task);
        await context.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(int id, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var task = new SyncTaskEntity { Id = id };
        context.SyncTasks.Attach(task);
        context.SyncTasks.Remove(task);
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Task was already removed by a parallel drain — treat as success.
        }
    }

    public async Task RemoveManyAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.SyncTasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task RemoveForAnimeAsync(int animeId, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.SyncTasks.Where(t => t.AnimeId == animeId).ExecuteDeleteAsync(ct);
    }
}
