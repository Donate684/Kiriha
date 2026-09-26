using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class SeasonalHiddenRepository : ISeasonalHiddenRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HashSet<int> _cachedIds = new();
    private readonly Lock _lock = new();
    private bool _initialized;

    public SeasonalHiddenRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var ids = await context.HiddenSeasonalAnime
            .AsNoTracking()
            .Select(x => x.AnimeId)
            .ToListAsync(ct);

        lock (_lock)
        {
            _cachedIds.Clear();
            foreach (var id in ids)
                _cachedIds.Add(id);
            _initialized = true;
        }
    }

    public IReadOnlySet<int> GetHiddenIds()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return new HashSet<int>(_cachedIds);
        }
    }

    public bool IsHidden(int animeId)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _cachedIds.Contains(animeId);
        }
    }

    public async Task AddAsync(int animeId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cachedIds.Add(animeId);
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var exists = await context.HiddenSeasonalAnime.AnyAsync(x => x.AnimeId == animeId, ct);
        if (!exists)
        {
            context.HiddenSeasonalAnime.Add(new HiddenSeasonalAnime
            {
                AnimeId = animeId,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveAsync(int animeId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cachedIds.Remove(animeId);
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.HiddenSeasonalAnime
            .Where(x => x.AnimeId == animeId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task RemoveRangeAsync(IEnumerable<int> animeIds, CancellationToken ct = default)
    {
        var idList = animeIds as List<int> ?? animeIds.ToList();
        if (idList.Count == 0) return;

        lock (_lock)
        {
            foreach (var id in idList)
                _cachedIds.Remove(id);
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.HiddenSeasonalAnime
            .Where(x => idList.Contains(x.AnimeId))
            .ExecuteDeleteAsync(ct);
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var ids = context.HiddenSeasonalAnime
                    .AsNoTracking()
                    .Select(x => x.AnimeId)
                    .ToList();
                _cachedIds.Clear();
                foreach (var id in ids)
                    _cachedIds.Add(id);
            }
            catch
            {
                // In case DB is not yet ready or migrating
            }
            finally
            {
                _initialized = true;
            }
        }
    }
}
