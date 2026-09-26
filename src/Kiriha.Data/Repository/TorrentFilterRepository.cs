using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repository;

public sealed class TorrentFilterRepository : ITorrentFilterRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HashSet<int> _cachedHiddenIds = new();
    private readonly Dictionary<int, AppSettings.TorrentFilterSet> _cachedFilters = new();
    private readonly Lock _lock = new();
    private bool _initialized;

    public TorrentFilterRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var hidden = await context.HiddenTorrentAnime
            .AsNoTracking()
            .Select(x => x.AnimeId)
            .ToListAsync(ct);

        var filters = await context.TorrentTitleFilters
            .AsNoTracking()
            .ToListAsync(ct);

        lock (_lock)
        {
            _cachedHiddenIds.Clear();
            foreach (var id in hidden)
                _cachedHiddenIds.Add(id);

            _cachedFilters.Clear();
            foreach (var f in filters)
                _cachedFilters[f.AnimeId] = MapToFilterSet(f);

            _initialized = true;
        }
    }

    public IReadOnlySet<int> GetHiddenAnimeIds()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return new HashSet<int>(_cachedHiddenIds);
        }
    }

    public bool IsAnimeHidden(int animeId)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _cachedHiddenIds.Contains(animeId);
        }
    }

    public async Task SetAnimeHiddenAsync(int animeId, bool hidden, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (hidden) _cachedHiddenIds.Add(animeId);
            else _cachedHiddenIds.Remove(animeId);
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        if (hidden)
        {
            var exists = await context.HiddenTorrentAnime.AnyAsync(x => x.AnimeId == animeId, ct);
            if (!exists)
            {
                context.HiddenTorrentAnime.Add(new HiddenTorrentAnime
                {
                    AnimeId = animeId,
                    CreatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync(ct);
            }
        }
        else
        {
            await context.HiddenTorrentAnime
                .Where(x => x.AnimeId == animeId)
                .ExecuteDeleteAsync(ct);
        }
    }

    public AppSettings.TorrentFilterSet? TryGetCachedFilter(int animeId)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _cachedFilters.TryGetValue(animeId, out var filter) ? Clone(filter) : null;
        }
    }

    public async Task<AppSettings.TorrentFilterSet?> GetFilterAsync(int animeId, CancellationToken ct = default)
    {
        var cached = TryGetCachedFilter(animeId);
        if (cached != null) return cached;

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TorrentTitleFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AnimeId == animeId, ct);

        if (entity == null) return null;
        var filterSet = MapToFilterSet(entity);
        lock (_lock)
        {
            _cachedFilters[animeId] = filterSet;
        }
        return Clone(filterSet);
    }

    public async Task SaveFilterAsync(int animeId, AppSettings.TorrentFilterSet filter, CancellationToken ct = default)
    {
        var cloned = Clone(filter);
        lock (_lock)
        {
            _cachedFilters[animeId] = cloned;
        }

        using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.TorrentTitleFilters
            .FirstOrDefaultAsync(x => x.AnimeId == animeId, ct);

        if (existing == null)
        {
            existing = new TorrentTitleFilter { AnimeId = animeId };
            MapFromFilterSet(existing, cloned);
            context.TorrentTitleFilters.Add(existing);
        }
        else
        {
            MapFromFilterSet(existing, cloned);
        }

        await context.SaveChangesAsync(ct);
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
                var hidden = context.HiddenTorrentAnime
                    .AsNoTracking()
                    .Select(x => x.AnimeId)
                    .ToList();
                var filters = context.TorrentTitleFilters
                    .AsNoTracking()
                    .ToList();

                _cachedHiddenIds.Clear();
                foreach (var id in hidden)
                    _cachedHiddenIds.Add(id);

                _cachedFilters.Clear();
                foreach (var f in filters)
                    _cachedFilters[f.AnimeId] = MapToFilterSet(f);
            }
            catch
            {
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    private static AppSettings.TorrentFilterSet MapToFilterSet(TorrentTitleFilter entity) => new()
    {
        OnlyCrunchyroll = entity.OnlyCrunchyroll,
        FilterNetflix = entity.FilterNetflix,
        FilterAmazon = entity.FilterAmazon,
        FilterHidive = entity.FilterHidive,
        FilterVaryg = entity.FilterVaryg,
        FilterEraiRaws = entity.FilterEraiRaws,
        FilterToonsHub = entity.FilterToonsHub,
        FilterJudas = entity.FilterJudas,
        FilterHevc = entity.FilterHevc,
        Filter1080p = entity.Filter1080p,
        UseCustomQuery = entity.UseCustomQuery,
        CustomQuery = entity.CustomQuery
    };

    private static void MapFromFilterSet(TorrentTitleFilter entity, AppSettings.TorrentFilterSet set)
    {
        entity.OnlyCrunchyroll = set.OnlyCrunchyroll;
        entity.FilterNetflix = set.FilterNetflix;
        entity.FilterAmazon = set.FilterAmazon;
        entity.FilterHidive = set.FilterHidive;
        entity.FilterVaryg = set.FilterVaryg;
        entity.FilterEraiRaws = set.FilterEraiRaws;
        entity.FilterToonsHub = set.FilterToonsHub;
        entity.FilterJudas = set.FilterJudas;
        entity.FilterHevc = set.FilterHevc;
        entity.Filter1080p = set.Filter1080p;
        entity.UseCustomQuery = set.UseCustomQuery;
        entity.CustomQuery = set.CustomQuery;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    private static AppSettings.TorrentFilterSet Clone(AppSettings.TorrentFilterSet set) => new()
    {
        OnlyCrunchyroll = set.OnlyCrunchyroll,
        FilterNetflix = set.FilterNetflix,
        FilterAmazon = set.FilterAmazon,
        FilterHidive = set.FilterHidive,
        FilterVaryg = set.FilterVaryg,
        FilterEraiRaws = set.FilterEraiRaws,
        FilterToonsHub = set.FilterToonsHub,
        FilterJudas = set.FilterJudas,
        FilterHevc = set.FilterHevc,
        Filter1080p = set.Filter1080p,
        UseCustomQuery = set.UseCustomQuery,
        CustomQuery = set.CustomQuery
    };
}
