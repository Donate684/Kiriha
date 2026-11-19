using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Franchise;

public sealed partial class FranchiseService
{
    public Task IngestFranchiseAsync(int sourceMalId, ShikiFranchiseResponse data, CancellationToken ct = default)
    {
        return IngestFranchiseResponseAsync(sourceMalId, data, ct);
    }

    private static bool IsAnimeNode(ShikiFranchiseNode node)
    {
        if (!string.IsNullOrEmpty(node.Url))
        {
            if (node.Url.Contains("/animes/", StringComparison.OrdinalIgnoreCase)) return true;
            if (node.Url.Contains("/mangas/", StringComparison.OrdinalIgnoreCase) ||
                node.Url.Contains("/ranobe/", StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (string.IsNullOrEmpty(node.Kind)) return true;

        return !string.Equals(node.Kind, "manga", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "ranobe", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "novel", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "light_novel", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "doujin", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "manhwa", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "manhua", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(node.Kind, "one_shot", StringComparison.OrdinalIgnoreCase);
    }

    private async Task IngestFranchiseResponseAsync(int requestedId, ShikiFranchiseResponse data, CancellationToken ct)
    {
        var animeNodes = data.Nodes != null
            ? data.Nodes.Where(IsAnimeNode).ToList()
            : [];

        var animeNodeIds = animeNodes.Select(n => n.Id).ToHashSet();
        var nodeNames = animeNodes.ToDictionary(n => n.Id, n => n.Name);

        var allRelations = new List<AnimeRelation>();

        if (data.Links != null)
        {
            foreach (var link in data.Links)
            {
                if (!animeNodeIds.Contains(link.SourceId) || !animeNodeIds.Contains(link.TargetId))
                    continue;

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
            groups[requestedId] = [];
        }

        foreach (var (srcId, rels) in groups)
        {
            await _relationRepo.ReplaceAsync(srcId, rels, ct).ConfigureAwait(false);
        }

        // Also ensure any other anime nodes in this franchise graph without outgoing links get their meta marked
        foreach (var node in animeNodes)
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

        await RebuildIndexAsync(ct).ConfigureAwait(false);
    }

    private static string ResolveDisplayTitle(AnimeEntity anime, bool useRussian) =>
        useRussian
            ? (!string.IsNullOrWhiteSpace(anime.RussianTitle) ? anime.RussianTitle : anime.Title)
            : (!string.IsNullOrWhiteSpace(anime.Title) ? anime.Title : (anime.RussianTitle ?? string.Empty));
}
