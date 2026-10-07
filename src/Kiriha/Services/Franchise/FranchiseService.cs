using System.Collections.Specialized;
using System.Threading.Channels;
using Avalonia.Threading;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
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
        IShikiApiService shikiApi,
        IBackgroundTaskSupervisor? backgroundTasks = null)
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

        if (backgroundTasks != null)
        {
            _ = backgroundTasks.Run("FranchiseService.ResolutionWorker", ct => ResolutionWorkerLoopAsync(ct), _cts.Token);
            _ = backgroundTasks.Run("FranchiseService.InitialBuild", async ct =>
            {
                try
                {
                    if (_animeRepo.InitializationTask != null)
                    {
                        await _animeRepo.InitializationTask.WaitAsync(ct).ConfigureAwait(false);
                    }
                    await RebuildIndexAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Graceful exit on shutdown
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FranchiseService: initial index build failed");
                }
            }, _cts.Token);
        }
        else
        {
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

    public Task IngestFranchiseAsync(int sourceMalId, ShikiFranchiseResponse data, CancellationToken ct = default)
    {
        return IngestFranchiseResponseAsync(sourceMalId, data, ct);
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
                .DistinctBy(x => x.Id)
                .ToDictionary(x => x.Id);

            if (userDict.Count == 0)
            {
                _index = new Dictionary<int, FranchiseContext>();
                IndexRebuilt?.Invoke();
                return;
            }

            var allRelations = await _relationRepo.GetAllAsync(ct).ConfigureAwait(false);
            var newIndex = new Dictionary<int, FranchiseContext>();

            // Build adjacency list for franchise graph
            var graph = new Dictionary<int, List<(int NeighborId, FranchiseRelationKind StepKind)>>();

            void AddEdge(int from, int to, FranchiseRelationKind kind)
            {
                if (!graph.TryGetValue(from, out var list))
                {
                    list = new List<(int, FranchiseRelationKind)>();
                    graph[from] = list;
                }
                list.Add((to, kind));
            }

            foreach (var r in allRelations)
            {
                if (r.SourceMalId <= 0 || r.TargetMalId <= 0) continue;
                if (string.Equals(r.RelationType, "character", StringComparison.OrdinalIgnoreCase)) continue;

                var directKind = MapDirectRelation(r.RelationType);
                var invertedKind = MapInvertedRelation(r.RelationType);

                AddEdge(r.SourceMalId, r.TargetMalId, directKind);
                AddEdge(r.TargetMalId, r.SourceMalId, invertedKind);
            }

            // Find connected components in graph
            var visitedNodes = new HashSet<int>();

            foreach (var startNode in graph.Keys)
            {
                if (!visitedNodes.Add(startNode)) continue;

                // Discover all nodes in this connected component
                var component = new List<int> { startNode };
                var compQueue = new Queue<int>();
                compQueue.Enqueue(startNode);

                while (compQueue.Count > 0)
                {
                    var curr = compQueue.Dequeue();
                    if (!graph.TryGetValue(curr, out var edges)) continue;
                    foreach (var (nextId, _) in edges)
                    {
                        if (visitedNodes.Add(nextId))
                        {
                            component.Add(nextId);
                            compQueue.Enqueue(nextId);
                        }
                    }
                }

                // Check which user library anime belong to this component
                var userAnimeInComp = component
                    .Where(id => userDict.ContainsKey(id))
                    .Select(id => userDict[id])
                    .ToList();

                if (userAnimeInComp.Count == 0) continue;

                var droppedList = userAnimeInComp.Where(x => x.Status == UserAnimeStatus.Dropped).ToList();
                var completedList = userAnimeInComp.Where(x => x.Status == UserAnimeStatus.Completed).ToList();
                var watchingList = userAnimeInComp.Where(x => x.Status == UserAnimeStatus.Watching).ToList();
                var planList = userAnimeInComp.Where(x => x.Status == UserAnimeStatus.PlanToWatch).ToList();
                var onHoldList = userAnimeInComp.Where(x => x.Status == UserAnimeStatus.OnHold).ToList();

                bool isMixed = droppedList.Count > 0 && completedList.Count > 0;

                // Run BFS from each user anime in this component to compute shortest distance & relation path
                // to all other nodes in the component
                var distancesFromUserAnime = new Dictionary<int, Dictionary<int, (int Distance, FranchiseRelationKind PathKind)>>();

                foreach (var userAnime in userAnimeInComp)
                {
                    var bfsVisited = new Dictionary<int, (int Distance, FranchiseRelationKind PathKind)>
                    {
                        [userAnime.Id] = (0, FranchiseRelationKind.None)
                    };
                    var bfsQueue = new Queue<(int NodeId, int Dist, FranchiseRelationKind RelSoFar)>();
                    bfsQueue.Enqueue((userAnime.Id, 0, FranchiseRelationKind.None));

                    while (bfsQueue.Count > 0)
                    {
                        var (curr, dist, relSoFar) = bfsQueue.Dequeue();
                        if (!graph.TryGetValue(curr, out var edges)) continue;

                        foreach (var (nextId, stepKind) in edges)
                        {
                            if (bfsVisited.ContainsKey(nextId)) continue;

                            var nextRel = dist == 0 ? stepKind : CombineRelations(relSoFar, stepKind);
                            bfsVisited[nextId] = (dist + 1, nextRel);
                            bfsQueue.Enqueue((nextId, dist + 1, nextRel));
                        }
                    }

                    distancesFromUserAnime[userAnime.Id] = bfsVisited;
                }

                (AnimeEntity Anime, int Distance, FranchiseRelationKind Relation)? FindClosest(int targetNodeId, List<AnimeEntity> candidates)
                {
                    AnimeEntity? bestAnime = null;
                    int bestDist = int.MaxValue;
                    FranchiseRelationKind bestRel = FranchiseRelationKind.None;

                    foreach (var c in candidates)
                    {
                        if (c.Id == targetNodeId) continue;

                        if (distancesFromUserAnime.TryGetValue(c.Id, out var map) &&
                            map.TryGetValue(targetNodeId, out var info))
                        {
                            if (info.Distance < bestDist)
                            {
                                bestDist = info.Distance;
                                bestAnime = c;
                                bestRel = info.PathKind;
                            }
                        }
                    }

                    if (bestAnime == null) return null;
                    return (bestAnime, bestDist, bestRel);
                }

                // Compute FranchiseContext for each node in the component
                foreach (var targetId in component)
                {
                    // If target anime is already in userDict and has direct active/finished status,
                    // direct status takes precedence on UI cards
                    if (userDict.TryGetValue(targetId, out var directUserAnime) &&
                        (directUserAnime.Status == UserAnimeStatus.Completed ||
                         directUserAnime.Status == UserAnimeStatus.Dropped ||
                         directUserAnime.Status == UserAnimeStatus.Watching))
                    {
                        continue;
                    }

                    if (isMixed)
                    {
                        var closestDrop = FindClosest(targetId, droppedList);
                        var closestComp = FindClosest(targetId, completedList);

                        if (closestDrop != null && closestComp != null)
                        {
                            var primary = closestComp.Value.Distance <= closestDrop.Value.Distance
                                ? closestComp.Value
                                : closestDrop.Value;

                            var rel = primary.Relation != FranchiseRelationKind.None && primary.Relation != FranchiseRelationKind.Other
                                ? primary.Relation
                                : (closestComp.Value.Relation != FranchiseRelationKind.None ? closestComp.Value.Relation : FranchiseRelationKind.Sequel);

                            var dropTitle = !string.IsNullOrWhiteSpace(closestDrop.Value.Anime.RussianTitle)
                                ? closestDrop.Value.Anime.RussianTitle
                                : closestDrop.Value.Anime.Title;

                            var compTitle = !string.IsNullOrWhiteSpace(closestComp.Value.Anime.RussianTitle)
                                ? closestComp.Value.Anime.RussianTitle
                                : closestComp.Value.Anime.Title;

                            var primTitle = !string.IsNullOrWhiteSpace(primary.Anime.RussianTitle)
                                ? primary.Anime.RussianTitle
                                : primary.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = primary.Anime.Status,
                                RelatedAnimeId = primary.Anime.Id,
                                RelatedTitle = primTitle,
                                RelatedProgress = primary.Anime.Progress,
                                RelatedTotalEpisodes = primary.Anime.TotalEpisodes,
                                RelatedScore = primary.Anime.Score,
                                IsMixed = true,
                                DroppedAnimeId = closestDrop.Value.Anime.Id,
                                DroppedTitle = dropTitle,
                                CompletedAnimeId = closestComp.Value.Anime.Id,
                                CompletedTitle = compTitle
                            };
                            continue;
                        }
                    }

                    if (droppedList.Count > 0 && completedList.Count == 0)
                    {
                        var closestDrop = FindClosest(targetId, droppedList);
                        if (closestDrop != null)
                        {
                            var rel = closestDrop.Value.Relation != FranchiseRelationKind.None && closestDrop.Value.Relation != FranchiseRelationKind.Other
                                ? closestDrop.Value.Relation
                                : FranchiseRelationKind.Sequel;

                            var title = !string.IsNullOrWhiteSpace(closestDrop.Value.Anime.RussianTitle)
                                ? closestDrop.Value.Anime.RussianTitle
                                : closestDrop.Value.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = UserAnimeStatus.Dropped,
                                RelatedAnimeId = closestDrop.Value.Anime.Id,
                                RelatedTitle = title,
                                RelatedProgress = closestDrop.Value.Anime.Progress,
                                RelatedTotalEpisodes = closestDrop.Value.Anime.TotalEpisodes,
                                RelatedScore = closestDrop.Value.Anime.Score,
                                IsMixed = false,
                                DroppedAnimeId = closestDrop.Value.Anime.Id,
                                DroppedTitle = title
                            };
                            continue;
                        }
                    }

                    if (completedList.Count > 0 && droppedList.Count == 0)
                    {
                        var closestComp = FindClosest(targetId, completedList);
                        if (closestComp != null)
                        {
                            var rel = closestComp.Value.Relation != FranchiseRelationKind.None && closestComp.Value.Relation != FranchiseRelationKind.Other
                                ? closestComp.Value.Relation
                                : FranchiseRelationKind.Sequel;

                            var title = !string.IsNullOrWhiteSpace(closestComp.Value.Anime.RussianTitle)
                                ? closestComp.Value.Anime.RussianTitle
                                : closestComp.Value.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = UserAnimeStatus.Completed,
                                RelatedAnimeId = closestComp.Value.Anime.Id,
                                RelatedTitle = title,
                                RelatedProgress = closestComp.Value.Anime.Progress,
                                RelatedTotalEpisodes = closestComp.Value.Anime.TotalEpisodes,
                                RelatedScore = closestComp.Value.Anime.Score,
                                IsMixed = false,
                                CompletedAnimeId = closestComp.Value.Anime.Id,
                                CompletedTitle = title
                            };
                            continue;
                        }
                    }

                    if (watchingList.Count > 0)
                    {
                        var closestWatch = FindClosest(targetId, watchingList);
                        if (closestWatch != null)
                        {
                            var rel = closestWatch.Value.Relation != FranchiseRelationKind.None && closestWatch.Value.Relation != FranchiseRelationKind.Other
                                ? closestWatch.Value.Relation
                                : FranchiseRelationKind.Sequel;

                            var title = !string.IsNullOrWhiteSpace(closestWatch.Value.Anime.RussianTitle)
                                ? closestWatch.Value.Anime.RussianTitle
                                : closestWatch.Value.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = UserAnimeStatus.Watching,
                                RelatedAnimeId = closestWatch.Value.Anime.Id,
                                RelatedTitle = title,
                                RelatedProgress = closestWatch.Value.Anime.Progress,
                                RelatedTotalEpisodes = closestWatch.Value.Anime.TotalEpisodes,
                                RelatedScore = closestWatch.Value.Anime.Score,
                                IsMixed = false
                            };
                            continue;
                        }
                    }

                    if (planList.Count > 0)
                    {
                        var closestPlan = FindClosest(targetId, planList);
                        if (closestPlan != null)
                        {
                            var rel = closestPlan.Value.Relation != FranchiseRelationKind.None && closestPlan.Value.Relation != FranchiseRelationKind.Other
                                ? closestPlan.Value.Relation
                                : FranchiseRelationKind.Sequel;

                            var title = !string.IsNullOrWhiteSpace(closestPlan.Value.Anime.RussianTitle)
                                ? closestPlan.Value.Anime.RussianTitle
                                : closestPlan.Value.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = UserAnimeStatus.PlanToWatch,
                                RelatedAnimeId = closestPlan.Value.Anime.Id,
                                RelatedTitle = title,
                                RelatedProgress = closestPlan.Value.Anime.Progress,
                                RelatedTotalEpisodes = closestPlan.Value.Anime.TotalEpisodes,
                                RelatedScore = closestPlan.Value.Anime.Score,
                                IsMixed = false
                            };
                            continue;
                        }
                    }

                    if (onHoldList.Count > 0)
                    {
                        var closestHold = FindClosest(targetId, onHoldList);
                        if (closestHold != null)
                        {
                            var rel = closestHold.Value.Relation != FranchiseRelationKind.None && closestHold.Value.Relation != FranchiseRelationKind.Other
                                ? closestHold.Value.Relation
                                : FranchiseRelationKind.Sequel;

                            var title = !string.IsNullOrWhiteSpace(closestHold.Value.Anime.RussianTitle)
                                ? closestHold.Value.Anime.RussianTitle
                                : closestHold.Value.Anime.Title;

                            newIndex[targetId] = new FranchiseContext
                            {
                                Relation = rel,
                                UserStatus = UserAnimeStatus.OnHold,
                                RelatedAnimeId = closestHold.Value.Anime.Id,
                                RelatedTitle = title,
                                RelatedProgress = closestHold.Value.Anime.Progress,
                                RelatedTotalEpisodes = closestHold.Value.Anime.TotalEpisodes,
                                RelatedScore = closestHold.Value.Anime.Score,
                                IsMixed = false
                            };
                        }
                    }
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

        return relationType.Trim().ToLowerInvariant() switch
        {
            "prequel" => FranchiseRelationKind.Sequel,
            "sequel" => FranchiseRelationKind.Prequel,
            "parent" or "parent_story" or "parent story" => FranchiseRelationKind.SpinOff,
            "side_story" or "side story" or "spin_off" or "spin-off" => FranchiseRelationKind.Parent,
            "summary" => FranchiseRelationKind.Summary,
            _ => FranchiseRelationKind.Other
        };
    }

    private static FranchiseRelationKind CombineRelations(FranchiseRelationKind current, FranchiseRelationKind next)
    {
        if (current == FranchiseRelationKind.SpinOff || next == FranchiseRelationKind.SpinOff)
            return FranchiseRelationKind.SpinOff;
        if (current == FranchiseRelationKind.SideStory || next == FranchiseRelationKind.SideStory)
            return FranchiseRelationKind.SideStory;
        if (current == FranchiseRelationKind.Summary || next == FranchiseRelationKind.Summary)
            return FranchiseRelationKind.Summary;
        if (current == FranchiseRelationKind.Sequel && next == FranchiseRelationKind.Sequel)
            return FranchiseRelationKind.Sequel;
        if (current == FranchiseRelationKind.Prequel && next == FranchiseRelationKind.Prequel)
            return FranchiseRelationKind.Prequel;
        if (current == FranchiseRelationKind.Parent || next == FranchiseRelationKind.Parent)
            return FranchiseRelationKind.Parent;
        if (current == FranchiseRelationKind.Sequel && (next == FranchiseRelationKind.Other || next == FranchiseRelationKind.None))
            return FranchiseRelationKind.Sequel;
        if ((current == FranchiseRelationKind.Other || current == FranchiseRelationKind.None) && next == FranchiseRelationKind.Sequel)
            return FranchiseRelationKind.Sequel;
        return FranchiseRelationKind.Other;
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
