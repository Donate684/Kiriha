using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Services.Franchise;

public sealed class FranchiseService : IFranchiseService, IDisposable
{
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeRelationRepository _relationRepo;
    private readonly IShikiApiService _shikiApi;
    private readonly Debouncer _rebuildDebouncer;
    private readonly CancellationTokenSource _cts = new();

    private readonly Channel<AnimeEntity> _resolutionQueue = Channel.CreateBounded<AnimeEntity>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private readonly HashSet<int> _queuedOrCheckedIds = new();
    private readonly Lock _queueLock = new();

    private IReadOnlyDictionary<int, FranchiseContext> _index = new Dictionary<int, FranchiseContext>();
    private bool _isDisposed;

    public event Action? IndexRebuilt;

    public FranchiseService(
        IAnimeRepository animeRepo,
        IAnimeRelationRepository relationRepo,
        IShikiApiService shikiApi)
    {
        _animeRepo = animeRepo;
        _relationRepo = relationRepo;
        _shikiApi = shikiApi;

        _rebuildDebouncer = new Debouncer(TimeSpan.FromMilliseconds(500), () =>
        {
            _ = RebuildIndexAsync();
        });

        if (_animeRepo.Collection != null)
        {
            _animeRepo.Collection.CollectionChanged += OnCollectionChanged;
        }

        // Start resolution worker loop in background
        _ = Task.Run(() => ResolutionWorkerLoopAsync(_cts.Token));

        // Initial background index build once repository initialization completes
        _ = Task.Run(async () =>
        {
            try
            {
                if (_animeRepo.InitializationTask != null)
                {
                    await _animeRepo.InitializationTask.ConfigureAwait(false);
                }
                await RebuildIndexAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FranchiseService: initial index build failed");
            }
        });
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isDisposed) return;
        _rebuildDebouncer.Invoke();
    }

    public FranchiseContext? GetFranchiseContext(int animeId)
    {
        return _index.TryGetValue(animeId, out var ctx) ? ctx : null;
    }

    public IReadOnlyDictionary<int, FranchiseContext> GetIndex() => _index;

    public void Enrich(IEnumerable<AnimeEntity> items)
    {
        if (items == null) return;
        var index = _index;
        foreach (var item in items)
        {
            item.Franchise = index.TryGetValue(item.Id, out var ctx) ? ctx : null;
        }
    }

    public void EnqueueForResolution(AnimeEntity item)
    {
        if (_isDisposed || item == null) return;
        if (item.Status == UserAnimeStatus.Completed || item.Status == UserAnimeStatus.Dropped) return;

        // Fast path: if already resolved in memory, apply immediately
        if (_index.TryGetValue(item.Id, out var existing))
        {
            if (item.Franchise != existing)
            {
                Dispatcher.UIThread.Post(() => item.Franchise = existing);
            }
            return;
        }

        lock (_queueLock)
        {
            if (_queuedOrCheckedIds.Add(item.Id))
            {
                _resolutionQueue.Writer.TryWrite(item);
            }
        }
    }

    private async Task ResolutionWorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var item = await _resolutionQueue.Reader.ReadAsync(ct).ConfigureAwait(false);
                if (item == null) continue;

                // Re-check index in case it was resolved by a prior franchise response
                if (_index.TryGetValue(item.Id, out var existing))
                {
                    Dispatcher.UIThread.Post(() => item.Franchise = existing);
                    continue;
                }

                // Check if we already have relation metadata stored in SQLite for this item
                var fetchedAt = await _relationRepo.GetFetchedAtAsync(item.Id, ct).ConfigureAwait(false);
                if (fetchedAt != null)
                {
                    if (_index.TryGetValue(item.Id, out var cachedCtx))
                    {
                        Dispatcher.UIThread.Post(() => item.Franchise = cachedCtx);
                    }
                    continue;
                }

                // Check if we already have relations stored in SQLite for this item
                var localRelations = await _relationRepo.GetBySourceIdAsync(item.Id, ct).ConfigureAwait(false);
                if (localRelations != null && localRelations.Count > 0)
                {
                    await RebuildIndexAsync(ct).ConfigureAwait(false);
                    if (_index.TryGetValue(item.Id, out var localCtx))
                    {
                        Dispatcher.UIThread.Post(() => item.Franchise = localCtx);
                    }
                    continue;
                }

                // Fetch franchise graph from Shikimori
                var data = await _shikiApi.GetFranchiseAsync(item.Id, ct).ConfigureAwait(false);
                if (data != null && data.Nodes != null && data.Links != null)
                {
                    await IngestFranchiseResponseAsync(item.Id, data, ct).ConfigureAwait(false);

                    if (_index.TryGetValue(item.Id, out var resolvedCtx))
                    {
                        Dispatcher.UIThread.Post(() => item.Franchise = resolvedCtx);
                    }
                }
                else
                {
                    // Standalone or not found: record empty relations to avoid repeated queries
                    await _relationRepo.ReplaceAsync(item.Id, [], ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FranchiseService: resolution loop step encountered an error");
            }
        }
    }

    public async Task IngestRelationsAsync(int sourceMalId, IEnumerable<AnimeRelation> relations, CancellationToken ct = default)
    {
        var relationList = relations as List<AnimeRelation> ?? relations.ToList();
        await _relationRepo.ReplaceAsync(sourceMalId, relationList, ct).ConfigureAwait(false);
        await RebuildIndexAsync(ct).ConfigureAwait(false);
    }

    private async Task IngestFranchiseResponseAsync(int requestedId, ShikiFranchiseResponse data, CancellationToken ct)
    {
        var nodeNames = data.Nodes?.ToDictionary(n => n.Id, n => n.Name) ?? new Dictionary<int, string>();
        var allRelations = new List<AnimeRelation>();

        if (data.Links != null)
        {
            foreach (var link in data.Links)
            {
                allRelations.Add(new AnimeRelation
                {
                    SourceMalId = link.SourceId,
                    TargetMalId = link.TargetId,
                    RelationType = link.Relation,
                    TargetName = nodeNames.TryGetValue(link.TargetId, out var name) ? name : string.Empty
                });
            }
        }

        // Group relations by SourceMalId so each anime's outgoing relations are properly replaced
        var groups = allRelations.GroupBy(r => r.SourceMalId).ToDictionary(g => g.Key, g => g.ToList());

        // Ensure requestedId has a record in AnimeRelationMeta even if it only has incoming links
        if (!groups.ContainsKey(requestedId))
        {
            groups[requestedId] = new List<AnimeRelation>();
        }

        foreach (var (srcId, rels) in groups)
        {
            await _relationRepo.ReplaceAsync(srcId, rels, ct).ConfigureAwait(false);
        }

        // Also ensure any other nodes in this franchise graph without outgoing links get their meta marked
        if (data.Nodes != null)
        {
            foreach (var node in data.Nodes)
            {
                if (!groups.ContainsKey(node.Id))
                {
                    var metaFetched = await _relationRepo.GetFetchedAtAsync(node.Id, ct).ConfigureAwait(false);
                    if (metaFetched == null)
                    {
                        await _relationRepo.ReplaceAsync(node.Id, [], ct).ConfigureAwait(false);
                    }
                }
            }
        }

        await RebuildIndexAsync(ct).ConfigureAwait(false);
    }

    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        try
        {
            var userAnimeList = await _animeRepo.GetSnapshotAsync().ConfigureAwait(false);
            if (userAnimeList == null || userAnimeList.Count == 0)
            {
                _index = new Dictionary<int, FranchiseContext>();
                IndexRebuilt?.Invoke();
                return;
            }

            var userDict = userAnimeList
                .Where(x => x.Status != UserAnimeStatus.None)
                .ToDictionary(x => x.Id);

            if (userDict.Count == 0)
            {
                _index = new Dictionary<int, FranchiseContext>();
                IndexRebuilt?.Invoke();
                return;
            }

            var allRelations = await _relationRepo.GetAllAsync(ct).ConfigureAwait(false);
            var newIndex = new Dictionary<int, FranchiseContext>();

            foreach (var r in allRelations)
            {
                // Case A: Source is in user's library
                if (userDict.TryGetValue(r.SourceMalId, out var sourceAnime) && r.TargetMalId > 0)
                {
                    var kind = MapDirectRelation(r.RelationType);
                    var candidate = CreateContext(kind, sourceAnime);
                    MergeContext(newIndex, r.TargetMalId, candidate);
                }

                // Case B: Target is in user's library
                if (userDict.TryGetValue(r.TargetMalId, out var targetAnime) && r.SourceMalId > 0)
                {
                    var kind = MapInvertedRelation(r.RelationType);
                    var candidate = CreateContext(kind, targetAnime);
                    MergeContext(newIndex, r.SourceMalId, candidate);
                }
            }

            _index = newIndex;
            Log.Debug("FranchiseService: built franchise index with {Count} entries", newIndex.Count);
            IndexRebuilt?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "FranchiseService: failed to rebuild index");
        }
    }

    private static FranchiseRelationKind MapDirectRelation(string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType)) return FranchiseRelationKind.Other;

        return relationType.Trim().ToLowerInvariant() switch
        {
            "sequel" => FranchiseRelationKind.Sequel,
            "prequel" => FranchiseRelationKind.Prequel,
            "side_story" or "side story" => FranchiseRelationKind.SideStory,
            "spin_off" or "spin-off" or "spinoff" => FranchiseRelationKind.SpinOff,
            "summary" => FranchiseRelationKind.Summary,
            "parent" or "parent_story" or "parent story" => FranchiseRelationKind.Parent,
            _ => FranchiseRelationKind.Other
        };
    }

    private static FranchiseRelationKind MapInvertedRelation(string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType)) return FranchiseRelationKind.Other;

        // Inverted: Target is the user's anime, Source is the candidate
        return relationType.Trim().ToLowerInvariant() switch
        {
            "prequel" => FranchiseRelationKind.Sequel, // Target is prequel to Source => Source is Sequel to Target
            "sequel" => FranchiseRelationKind.Prequel,  // Target is sequel to Source => Source is Prequel to Target
            "parent" or "parent_story" or "parent story" => FranchiseRelationKind.SpinOff,
            "side_story" or "side story" or "spin_off" or "spin-off" => FranchiseRelationKind.Parent,
            "summary" => FranchiseRelationKind.Summary,
            _ => FranchiseRelationKind.Other
        };
    }

    private static FranchiseContext CreateContext(FranchiseRelationKind kind, AnimeEntity relatedUserAnime)
    {
        var title = !string.IsNullOrWhiteSpace(relatedUserAnime.RussianTitle)
            ? relatedUserAnime.RussianTitle
            : relatedUserAnime.Title;

        return new FranchiseContext
        {
            Relation = kind,
            UserStatus = relatedUserAnime.Status,
            RelatedAnimeId = relatedUserAnime.Id,
            RelatedTitle = title,
            RelatedProgress = relatedUserAnime.Progress,
            RelatedTotalEpisodes = relatedUserAnime.TotalEpisodes,
            RelatedScore = relatedUserAnime.Score
        };
    }

    private static void MergeContext(Dictionary<int, FranchiseContext> index, int targetId, FranchiseContext candidate)
    {
        if (!index.TryGetValue(targetId, out var existing))
        {
            index[targetId] = candidate;
            return;
        }

        // Prioritization:
        // 1. Dropped status has highest alert priority (warning the user)
        // 2. Direct Sequel with Completed
        // 3. Any Completed
        // 4. Watching
        // 5. PlanToWatch
        int existingScore = GetScore(existing);
        int candidateScore = GetScore(candidate);

        if (candidateScore > existingScore)
        {
            index[targetId] = candidate;
        }
    }

    private static int GetScore(FranchiseContext ctx)
    {
        int score = 0;
        if (ctx.UserStatus == UserAnimeStatus.Dropped) score += 100;
        else if (ctx.UserStatus == UserAnimeStatus.Completed) score += 80;
        else if (ctx.UserStatus == UserAnimeStatus.Watching) score += 70;
        else if (ctx.UserStatus == UserAnimeStatus.PlanToWatch) score += 60;
        else if (ctx.UserStatus == UserAnimeStatus.OnHold) score += 50;

        if (ctx.Relation == FranchiseRelationKind.Sequel) score += 15;
        else if (ctx.Relation == FranchiseRelationKind.Prequel) score += 10;
        else if (ctx.Relation == FranchiseRelationKind.SpinOff) score += 5;

        return score;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();
        _cts.Dispose();

        if (_animeRepo.Collection != null)
        {
            _animeRepo.Collection.CollectionChanged -= OnCollectionChanged;
        }
        _rebuildDebouncer.Dispose();
    }
}
