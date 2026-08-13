using Kiriha.Services.Data.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public HistoryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(HistoryItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.History.Add(item);
        await context.SaveChangesAsync();
    }

    public async Task<List<HistoryItem>> GetAsync(int limit = 1000)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.History
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
