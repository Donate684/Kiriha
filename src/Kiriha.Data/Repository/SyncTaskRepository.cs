using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class SyncTaskRepository : ISyncTaskRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SyncTaskRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> AddAsync(SyncTaskEntity task)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncTasks.Add(task);
        await context.SaveChangesAsync();
        return task.Id;
    }

    public async Task<List<SyncTaskEntity>> GetPendingAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncTasks.OrderBy(t => t.Id).ToListAsync();
    }

    public async Task UpdateAsync(SyncTaskEntity task)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncTasks.Update(task);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var task = new SyncTaskEntity { Id = id };
        context.SyncTasks.Attach(task);
        context.SyncTasks.Remove(task);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Task was already removed by a parallel drain — treat as success.
        }
    }

    public async Task RemoveManyAsync(IEnumerable<int> ids)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await context.SyncTasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
    }

    public async Task RemoveForAnimeAsync(int animeId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var tasks = await context.SyncTasks.Where(t => t.AnimeId == animeId).ToListAsync();
        if (tasks.Count == 0) return;
        context.SyncTasks.RemoveRange(tasks);
        await context.SaveChangesAsync();
    }
}
