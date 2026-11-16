using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Data.Repository;

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SemaphoreSlim _statusLock = new(1, 1);

    public HistoryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(HistoryItem item, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.History.Add(item);
        await context.SaveChangesAsync(ct);
    }

    public async Task<List<HistoryItem>> GetAsync(int limit = 1000, CancellationToken ct = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.History
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task UpdateTrackerStatusAsync(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default)
        => UpdateTrackerStatusAsync(animeId, MediaKind.Anime, episode, trackerName, state, error, ct);

    public async Task UpdateTrackerStatusAsync(int animeId, MediaKind mediaKind, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default)
    {
        await _statusLock.WaitAsync(ct);
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var query = context.History.Where(h => h.AnimeId == animeId && h.MediaKind == mediaKind);
            if (episode.HasValue && episode.Value > 0)
            {
                query = query.Where(h => h.Episode == episode.Value);
            }

            var item = await query.OrderByDescending(h => h.Timestamp).FirstOrDefaultAsync(ct);
            if (item != null)
            {
                item.SetTrackerStatus(trackerName, state, error);
                context.History.Update(item);
                await context.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public Task SetPendingTrackersAsync(int animeId, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default)
        => SetPendingTrackersAsync(animeId, MediaKind.Anime, episode, trackerNames, ct);

    public async Task SetPendingTrackersAsync(int animeId, MediaKind mediaKind, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default)
    {
        var trackers = trackerNames.ToList();
        if (trackers.Count == 0) return;

        await _statusLock.WaitAsync(ct);
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var query = context.History.Where(h => h.AnimeId == animeId && h.MediaKind == mediaKind);
            if (episode.HasValue && episode.Value > 0)
            {
                query = query.Where(h => h.Episode == episode.Value);
            }

            var item = await query.OrderByDescending(h => h.Timestamp).FirstOrDefaultAsync(ct);
            if (item != null)
            {
                foreach (var tracker in trackers)
                {
                    if (!item.TrackerStatuses.TryGetValue(tracker, out var existing) || existing.State != TrackerSyncState.Success)
                    {
                        item.SetTrackerStatus(tracker, TrackerSyncState.Pending);
                    }
                }
                context.History.Update(item);
                await context.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _statusLock.Release();
        }
    }
}
