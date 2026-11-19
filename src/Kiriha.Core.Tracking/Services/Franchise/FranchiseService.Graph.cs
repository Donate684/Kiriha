using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Franchise;

public sealed partial class FranchiseService
{
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
                .Where(x => x.MediaKind == MediaKind.Anime && x.Status != UserAnimeStatus.None)
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
                    list = [];
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

                            bool useRussian = _settingsService?.Current.UI.UseRussianTitles ?? true;
                            var dropTitle = ResolveDisplayTitle(closestDrop.Value.Anime, useRussian);
                            var compTitle = ResolveDisplayTitle(closestComp.Value.Anime, useRussian);
                            var primTitle = ResolveDisplayTitle(primary.Anime, useRussian);

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

                            var title = ResolveDisplayTitle(closestDrop.Value.Anime, _settingsService?.Current.UI.UseRussianTitles ?? true);

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

                            var title = ResolveDisplayTitle(closestComp.Value.Anime, _settingsService?.Current.UI.UseRussianTitles ?? true);

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

                            var title = ResolveDisplayTitle(closestWatch.Value.Anime, _settingsService?.Current.UI.UseRussianTitles ?? true);

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

                            var title = ResolveDisplayTitle(closestPlan.Value.Anime, _settingsService?.Current.UI.UseRussianTitles ?? true);

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

                            var title = ResolveDisplayTitle(closestHold.Value.Anime, _settingsService?.Current.UI.UseRussianTitles ?? true);

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
}
