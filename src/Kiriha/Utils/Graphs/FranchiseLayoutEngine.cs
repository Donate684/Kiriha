using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Localization;

namespace Kiriha.Utils.Graphs;

public partial class FranchiseGraphVisualNode : ObservableObject
{
    public ShikiFranchiseNode Node { get; set; } = null!;
    public double X { get; set; }
    public double Y { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public bool IsCurrent { get; set; }
    public int StepIndex { get; set; }

    [ObservableProperty]
    private bool _isLastInTimeline;

    public double NodeWidth { get; set; } = 280;
    public double NodeHeight { get; set; } = 104;

    [ObservableProperty]
    private string _displayImageUrl = string.Empty;

    [ObservableProperty]
    private UserAnimeStatus _userStatus = UserAnimeStatus.None;

    [ObservableProperty]
    private int? _userProgress;

    [ObservableProperty]
    private int? _totalEpisodes;

    [ObservableProperty]
    private string? _russianTitle;

    [ObservableProperty]
    private string? _score;

    public bool IsMainLine { get; set; }
    public bool IsSpecial { get; set; }
    public bool IsMangaOrNovel { get; set; }
    public string RoleBadge { get; set; } = string.Empty;

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RussianTitle) ? RussianTitle : Node.Name;

    public string SubTitle => !string.IsNullOrWhiteSpace(RussianTitle) && !string.Equals(RussianTitle, Node.Name, StringComparison.OrdinalIgnoreCase)
        ? Node.Name
        : (Node.Year.HasValue ? Node.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

    public string KindBadge => Node.Kind?.ToLowerInvariant() switch
    {
        "tv" => "TV",
        "movie" => LocalizationStore.Translate("anime.labels.franchise_role_movie"),
        "ova" => "OVA",
        "ona" => "ONA",
        "special" => LocalizationStore.Translate("anime.labels.franchise_role_special"),
        "manga" => LocalizationStore.Translate("anime.labels.franchise_role_manga"),
        "light_novel" or "novel" => LocalizationStore.Translate("anime.labels.franchise_role_novel"),
        _ => !string.IsNullOrEmpty(Node.Kind) ? char.ToUpper(Node.Kind[0], CultureInfo.InvariantCulture) + Node.Kind[1..] : "Anime"
    };

    public bool HasProgress => TotalEpisodes.HasValue && TotalEpisodes.Value > 0 && UserProgress.HasValue && UserProgress.Value > 0;

    public double ProgressFraction => TotalEpisodes.HasValue && TotalEpisodes.Value > 0 && UserProgress.HasValue
        ? Math.Clamp((double)UserProgress.Value / TotalEpisodes.Value, 0.0, 1.0)
        : 0.0;

    public string ProgressBadgeText
    {
        get
        {
            if (UserStatus == UserAnimeStatus.Completed)
                return LocalizationStore.Translate("anime.status.completed");
            if (UserStatus == UserAnimeStatus.Watching)
            {
                var epAbbr = LocalizationStore.Translate("anime.labels.ep_abbr");
                return TotalEpisodes.HasValue && TotalEpisodes.Value > 0
                    ? $"{UserProgress ?? 0} / {TotalEpisodes.Value} {epAbbr}"
                    : $"{UserProgress ?? 0} {epAbbr}";
            }
            if (UserStatus == UserAnimeStatus.PlanToWatch)
                return LocalizationStore.Translate("anime.status.plan_to_watch");
            if (UserStatus == UserAnimeStatus.OnHold)
                return LocalizationStore.Translate("anime.status.on_hold");
            if (UserStatus == UserAnimeStatus.Dropped)
                return LocalizationStore.Translate("anime.status.dropped");
            if (TotalEpisodes.HasValue && TotalEpisodes.Value > 0)
            {
                var epAbbr = LocalizationStore.Translate("anime.labels.ep_abbr");
                return $"{TotalEpisodes.Value} {epAbbr}";
            }
            return string.Empty;
        }
    }

    public Avalonia.Point TopPoint => new Avalonia.Point(X + NodeWidth / 2, Y);
    public Avalonia.Point BottomPoint => new Avalonia.Point(X + NodeWidth / 2, Y + NodeHeight);
    public Avalonia.Point LeftPoint => new Avalonia.Point(X, Y + NodeHeight / 2);
    public Avalonia.Point RightPoint => new Avalonia.Point(X + NodeWidth, Y + NodeHeight / 2);
    public Avalonia.Point CenterPoint => new Avalonia.Point(X + NodeWidth / 2, Y + NodeHeight / 2);
}

public class FranchiseGraphVisualLink
{
    public ShikiFranchiseLink Link { get; set; } = null!;
    public FranchiseGraphVisualNode Source { get; set; } = null!;
    public FranchiseGraphVisualNode Target { get; set; } = null!;
    public string ConnectionPath { get; set; } = string.Empty;
    public string ArrowPath { get; set; } = string.Empty;
    public double LabelX { get; set; }
    public double LabelY { get; set; }
    public string RelationLabel { get; set; } = string.Empty;
    public bool HasLabel => !string.IsNullOrEmpty(RelationLabel);
    public bool IsMainLine { get; set; }
}

public class FranchiseLayoutOptions
{
    public bool HideSpecials { get; set; } = true;
    public bool AnimeOnly { get; set; } = true;
}

public class FranchiseGraphLayout
{
    public List<FranchiseGraphVisualNode> Nodes { get; set; } = new();
    public List<FranchiseGraphVisualLink> Links { get; set; } = new();
    public List<FranchiseGraphVisualNode> TimelineNodes { get; set; } = new();
    public double Width { get; set; }
    public double Height { get; set; }
}

public static class FranchiseLayoutEngine
{
    public static FranchiseGraphLayout CalculateLayout(
        ShikiFranchiseResponse data,
        FranchiseLayoutOptions? options = null,
        double cellWidth = 380,
        double cellHeight = 160)
    {
        options ??= new FranchiseLayoutOptions();

        // 1. Filter nodes based on user options
        var candidateNodes = data.Nodes.Where(n =>
        {
            if (n.Id == data.CurrentId) return true; // Never hide active node
            if (options.AnimeOnly && IsMangaOrNovelKind(n.Kind)) return false;
            if (options.HideSpecials && IsSpecialOrShortNode(n)) return false;
            return true;
        }).ToList();

        var nodeSet = new HashSet<int>(candidateNodes.Select(n => n.Id));

        var visualNodes = candidateNodes.ToDictionary(
            n => n.Id,
            n => new FranchiseGraphVisualNode
            {
                Node = n,
                IsCurrent = n.Id == data.CurrentId,
                IsSpecial = IsSpecialOrShortNode(n),
                IsMangaOrNovel = IsMangaOrNovelKind(n.Kind)
            });

        // Filter valid links where both source and target are kept
        var validLinks = data.Links
            .Where(l => nodeSet.Contains(l.SourceId) && nodeSet.Contains(l.TargetId))
            .ToList();

        // 2. Identify the Main Spine (Primary Storyline / Canon TV series)
        var spine = FindMainSpine(candidateNodes, validLinks, data.CurrentId);
        var spineIds = new HashSet<int>(spine.Select(n => n.Id));

        foreach (var spineNode in spine)
        {
            if (visualNodes.TryGetValue(spineNode.Id, out var vn))
            {
                vn.IsMainLine = true;
            }
        }

        // Set Role badges
        foreach (var vn in visualNodes.Values)
        {
            vn.RoleBadge = GetRoleBadge(vn.Node, vn.IsMainLine);
        }

        // 3. Coordinate Assignment:
        // Place spine along GridX = 0, spaced by 2 Y-levels to leave room for intermediate releases
        var occupiedGrid = new HashSet<(int x, int y)>();
        int currentSpineY = 0;

        for (int i = 0; i < spine.Count; i++)
        {
            var node = visualNodes[spine[i].Id];
            node.GridX = 0;
            node.GridY = currentSpineY;
            occupiedGrid.Add((0, currentSpineY));
            currentSpineY += 2;
        }

        // Now place non-spine nodes relative to spine nodes
        var placedNodeIds = new HashSet<int>(spineIds);

        // Place direct children/relations of spine nodes
        foreach (var spineNode in spine)
        {
            var spineVisual = visualNodes[spineNode.Id];
            var spineLinks = validLinks.Where(l => l.SourceId == spineNode.Id || l.TargetId == spineNode.Id).ToList();

            foreach (var link in spineLinks)
            {
                int neighborId = link.SourceId == spineNode.Id ? link.TargetId : link.SourceId;
                if (placedNodeIds.Contains(neighborId)) continue;
                if (!visualNodes.TryGetValue(neighborId, out var neighborVisual)) continue;

                bool isMovie = string.Equals(neighborVisual.Node.Kind, "movie", StringComparison.OrdinalIgnoreCase);

                // Target X: Movies go to right (+1), small specials or chibi to left (-1) or right (+1)
                int targetX = isMovie ? 1 : 1;
                if (occupiedGrid.Contains((targetX, spineVisual.GridY)))
                {
                    targetX = -1;
                }
                if (occupiedGrid.Contains((targetX, spineVisual.GridY)))
                {
                    targetX = 2;
                }

                // Target Y: If it's a sequel or released between this season and next season, place at spineVisual.GridY + 1
                int targetY = spineVisual.GridY;
                if ((link.Relation == "sequel" || isMovie) && !occupiedGrid.Contains((targetX, spineVisual.GridY + 1)))
                {
                    targetY = spineVisual.GridY + 1;
                }
                else if (occupiedGrid.Contains((targetX, targetY)))
                {
                    targetY = spineVisual.GridY + 1;
                }

                // If still occupied, find first free slot
                while (occupiedGrid.Contains((targetX, targetY)))
                {
                    targetX = targetX > 0 ? targetX + 1 : targetX - 1;
                }

                neighborVisual.GridX = targetX;
                neighborVisual.GridY = targetY;
                occupiedGrid.Add((targetX, targetY));
                placedNodeIds.Add(neighborId);

                // Place consecutive sequels of this side node in the same column downwards
                PlaceSideBranchSequels(neighborVisual, validLinks, visualNodes, occupiedGrid, placedNodeIds);
            }
        }

        // Place any remaining unplaced nodes (e.g. disconnected or distant nodes)
        var remainingNodes = visualNodes.Values
            .Where(n => !placedNodeIds.Contains(n.Node.Id))
            .OrderBy(n => n.Node.Date)
            .ThenBy(n => n.Node.Year)
            .ToList();

        int fallbackY = 0;
        foreach (var rem in remainingNodes)
        {
            int gx = 1;
            while (occupiedGrid.Contains((gx, fallbackY)))
            {
                gx++;
                if (gx > 3)
                {
                    gx = -1;
                    while (occupiedGrid.Contains((gx, fallbackY)))
                    {
                        gx--;
                    }
                    break;
                }
            }
            rem.GridX = gx;
            rem.GridY = fallbackY;
            occupiedGrid.Add((gx, fallbackY));
            placedNodeIds.Add(rem.Node.Id);
            fallbackY += 2;
        }

        // 4. Convert Grid coordinates to Canvas coordinates
        int minX = visualNodes.Values.Count > 0 ? visualNodes.Values.Min(n => n.GridX) : 0;
        int minY = visualNodes.Values.Count > 0 ? visualNodes.Values.Min(n => n.GridY) : 0;

        double offsetX = -minX * cellWidth + 90;
        double offsetY = -minY * cellHeight + 90;

        foreach (var node in visualNodes.Values)
        {
            node.X = node.GridX * cellWidth + offsetX;
            node.Y = node.GridY * cellHeight + offsetY;
        }

        // 5. Build visual links with paths and arrows
        var visualLinks = new List<FranchiseGraphVisualLink>();
        foreach (var link in validLinks)
        {
            if (visualNodes.TryGetValue(link.SourceId, out var src) &&
                visualNodes.TryGetValue(link.TargetId, out var tgt))
            {
                // Ensure proper direction from earlier/parent to later/child
                var sourceNode = src;
                var targetNode = tgt;

                if (link.Relation == "prequel" || link.Relation == "parent_story")
                {
                    sourceNode = tgt;
                    targetNode = src;
                }

                var (connPath, arrowPath, labelX, labelY) = BuildConnectionGeometry(sourceNode, targetNode);

                visualLinks.Add(new FranchiseGraphVisualLink
                {
                    Link = link,
                    Source = sourceNode,
                    Target = targetNode,
                    ConnectionPath = connPath,
                    ArrowPath = arrowPath,
                    LabelX = labelX,
                    LabelY = labelY,
                    RelationLabel = GetRelationLabel(link.Relation, targetNode.Node.Kind),
                    IsMainLine = sourceNode.IsMainLine && targetNode.IsMainLine
                });
            }
        }

        // 6. Generate Timeline Nodes (Watch Order)
        var timelineList = visualNodes.Values
            .OrderBy(n => n.Node.Date > 0 ? n.Node.Date : (n.Node.Year ?? 0) * 10000L)
            .ThenBy(n => n.Node.Id)
            .ToList();

        for (int i = 0; i < timelineList.Count; i++)
        {
            timelineList[i].StepIndex = i + 1;
            timelineList[i].IsLastInTimeline = i == timelineList.Count - 1;
        }

        double maxX = visualNodes.Values.Count > 0 ? visualNodes.Values.Max(n => n.X) : 0;
        double maxY = visualNodes.Values.Count > 0 ? visualNodes.Values.Max(n => n.Y) : 0;

        return new FranchiseGraphLayout
        {
            Nodes = visualNodes.Values.ToList(),
            Links = visualLinks,
            TimelineNodes = timelineList,
            Width = maxX + cellWidth + 120,
            Height = maxY + cellHeight + 120
        };
    }

    private static void PlaceSideBranchSequels(
        FranchiseGraphVisualNode parent,
        List<ShikiFranchiseLink> validLinks,
        Dictionary<int, FranchiseGraphVisualNode> visualNodes,
        HashSet<(int x, int y)> occupiedGrid,
        HashSet<int> placedNodeIds)
    {
        var nextSequels = validLinks
            .Where(l => l.SourceId == parent.Node.Id && (l.Relation == "sequel" || l.Relation == "full_story"))
            .Select(l => l.TargetId)
            .Where(id => !placedNodeIds.Contains(id))
            .ToList();

        int currentY = parent.GridY + 1;
        foreach (var seqId in nextSequels)
        {
            if (!visualNodes.TryGetValue(seqId, out var seqVisual)) continue;

            int targetX = parent.GridX;
            int targetY = currentY;

            while (occupiedGrid.Contains((targetX, targetY)))
            {
                targetY++;
            }

            seqVisual.GridX = targetX;
            seqVisual.GridY = targetY;
            occupiedGrid.Add((targetX, targetY));
            placedNodeIds.Add(seqId);

            currentY = targetY + 1;
            PlaceSideBranchSequels(seqVisual, validLinks, visualNodes, occupiedGrid, placedNodeIds);
        }
    }

    private static (string connectionPath, string arrowPath, double labelX, double labelY) BuildConnectionGeometry(
        FranchiseGraphVisualNode src,
        FranchiseGraphVisualNode tgt)
    {
        Avalonia.Point p1, p2;
        string connPath;
        string arrowPath;
        double labelX, labelY;

        // Same column, direct vertical down
        if (src.GridX == tgt.GridX && src.GridY < tgt.GridY)
        {
            p1 = src.BottomPoint;
            p2 = tgt.TopPoint;

            connPath = FormattableString.Invariant($"M {p1.X:F1},{p1.Y:F1} L {p2.X:F1},{p2.Y:F1}");
            arrowPath = FormattableString.Invariant($"M {p2.X - 5:F1},{p2.Y - 7:F1} L {p2.X:F1},{p2.Y:F1} L {p2.X + 5:F1},{p2.Y - 7:F1} Z");
            labelX = (p1.X + p2.X) / 2 - 28;
            labelY = (p1.Y + p2.Y) / 2 - 9;
        }
        // Branching to the right
        else if (src.GridX < tgt.GridX)
        {
            p1 = src.RightPoint;
            p2 = tgt.LeftPoint;

            double curveOffset = Math.Max(40, Math.Abs(p2.X - p1.X) * 0.4);
            var c1 = new Avalonia.Point(p1.X + curveOffset, p1.Y);
            var c2 = new Avalonia.Point(p2.X - curveOffset, p2.Y);

            connPath = FormattableString.Invariant($"M {p1.X:F1},{p1.Y:F1} C {c1.X:F1},{c1.Y:F1} {c2.X:F1},{c2.Y:F1} {p2.X:F1},{p2.Y:F1}");
            arrowPath = FormattableString.Invariant($"M {p2.X - 7:F1},{p2.Y - 5:F1} L {p2.X:F1},{p2.Y:F1} L {p2.X - 7:F1},{p2.Y + 5:F1} Z");
            labelX = (p1.X + p2.X) / 2 - 28;
            labelY = (p1.Y + p2.Y) / 2 - 9;
        }
        // Branching to the left
        else if (src.GridX > tgt.GridX)
        {
            p1 = src.LeftPoint;
            p2 = tgt.RightPoint;

            double curveOffset = Math.Max(40, Math.Abs(p1.X - p2.X) * 0.4);
            var c1 = new Avalonia.Point(p1.X - curveOffset, p1.Y);
            var c2 = new Avalonia.Point(p2.X + curveOffset, p2.Y);

            connPath = FormattableString.Invariant($"M {p1.X:F1},{p1.Y:F1} C {c1.X:F1},{c1.Y:F1} {c2.X:F1},{c2.Y:F1} {p2.X:F1},{p2.Y:F1}");
            arrowPath = FormattableString.Invariant($"M {p2.X + 7:F1},{p2.Y - 5:F1} L {p2.X:F1},{p2.Y:F1} L {p2.X + 7:F1},{p2.Y + 5:F1} Z");
            labelX = (p1.X + p2.X) / 2 - 28;
            labelY = (p1.Y + p2.Y) / 2 - 9;
        }
        // Below to above or fallback
        else
        {
            p1 = src.TopPoint;
            p2 = tgt.BottomPoint;

            double curveOffset = 40;
            var c1 = new Avalonia.Point(p1.X, p1.Y - curveOffset);
            var c2 = new Avalonia.Point(p2.X, p2.Y + curveOffset);

            connPath = FormattableString.Invariant($"M {p1.X:F1},{p1.Y:F1} C {c1.X:F1},{c1.Y:F1} {c2.X:F1},{c2.Y:F1} {p2.X:F1},{p2.Y:F1}");
            arrowPath = FormattableString.Invariant($"M {p2.X - 5:F1},{p2.Y + 7:F1} L {p2.X:F1},{p2.Y:F1} L {p2.X + 5:F1},{p2.Y + 7:F1} Z");
            labelX = (p1.X + p2.X) / 2 - 28;
            labelY = (p1.Y + p2.Y) / 2 - 9;
        }

        return (connPath, arrowPath, labelX, labelY);
    }

    private static List<ShikiFranchiseNode> FindMainSpine(
        List<ShikiFranchiseNode> nodes,
        List<ShikiFranchiseLink> links,
        int currentId)
    {
        // 1. Build directed graph for sequels: A -> B
        var forward = new Dictionary<int, List<int>>();
        var inDegree = new Dictionary<int, int>();

        foreach (var n in nodes)
        {
            forward[n.Id] = new List<int>();
            inDegree[n.Id] = 0;
        }

        foreach (var link in links)
        {
            int from = -1;
            int to = -1;

            if (link.Relation == "sequel" || link.Relation == "full_story")
            {
                from = link.SourceId;
                to = link.TargetId;
            }
            else if (link.Relation == "prequel" || link.Relation == "parent_story")
            {
                from = link.TargetId;
                to = link.SourceId;
            }

            if (from != -1 && to != -1 && forward.ContainsKey(from) && forward.ContainsKey(to))
            {
                forward[from].Add(to);
                inDegree[to]++;
            }
        }

        // 2. Find chains starting from nodes with in-degree 0 (or smallest in-degree)
        var nodeMap = nodes.ToDictionary(n => n.Id);
        var roots = nodes.Where(n => inDegree[n.Id] == 0).ToList();
        if (roots.Count == 0 && nodes.Count > 0)
        {
            roots.Add(nodes.OrderBy(n => n.Date).ThenBy(n => n.Year).First());
        }

        List<int>? bestChain = null;
        int bestScore = -1;

        foreach (var root in roots)
        {
            var chain = new List<int>();
            var visited = new HashSet<int>();
            int curr = root.Id;

            while (curr != 0 && visited.Add(curr))
            {
                chain.Add(curr);
                var nextCandidates = forward[curr].Where(x => !visited.Contains(x)).ToList();
                if (nextCandidates.Count == 0) break;

                // Pick the best next candidate: prefer TV, then highest weight / earliest date
                curr = nextCandidates
                    .OrderByDescending(id => nodeMap.TryGetValue(id, out var n) && string.Equals(n.Kind, "tv", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(id => nodeMap.TryGetValue(id, out var n) ? n.Weight : 0)
                    .ThenBy(id => nodeMap.TryGetValue(id, out var n) ? n.Date : 0)
                    .First();
            }

            // Score this chain: length + boost if it contains currentId + boost for TV nodes
            int score = chain.Count * 10;
            if (chain.Contains(currentId)) score += 50;
            score += chain.Count(id => nodeMap.TryGetValue(id, out var n) && string.Equals(n.Kind, "tv", StringComparison.OrdinalIgnoreCase)) * 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestChain = chain;
            }
        }

        if (bestChain == null || bestChain.Count == 0)
        {
            var fallback = nodes.FirstOrDefault(n => n.Id == currentId) ?? nodes.FirstOrDefault();
            return fallback != null ? new List<ShikiFranchiseNode> { fallback } : new List<ShikiFranchiseNode>();
        }

        return bestChain.Select(id => nodeMap[id]).ToList();
    }

    private static string GetRoleBadge(ShikiFranchiseNode node, bool isMainSpine)
    {
        if (IsMangaOrNovelKind(node.Kind))
            return LocalizationStore.Translate("anime.labels.franchise_role_manga");

        if (isMainSpine)
            return LocalizationStore.Translate("anime.labels.franchise_role_main");

        if (string.Equals(node.Kind, "movie", StringComparison.OrdinalIgnoreCase))
            return LocalizationStore.Translate("anime.labels.franchise_role_movie");

        if (IsSpecialOrShortNode(node))
            return LocalizationStore.Translate("anime.labels.franchise_role_special");

        if (string.Equals(node.Kind, "ova", StringComparison.OrdinalIgnoreCase))
            return LocalizationStore.Translate("anime.labels.franchise_role_sidestory");

        return LocalizationStore.Translate("anime.labels.franchise_role_spinoff");
    }

    private static string GetRelationLabel(string relation, string targetKind)
    {
        if (string.Equals(targetKind, "movie", StringComparison.OrdinalIgnoreCase))
            return LocalizationStore.Translate("anime.labels.franchise_role_movie");

        return relation.ToLowerInvariant() switch
        {
            "sequel" => LocalizationStore.Translate("anime.labels.franchise_sequel"),
            "prequel" => LocalizationStore.Translate("anime.labels.franchise_prequel"),
            "side_story" => LocalizationStore.Translate("anime.labels.franchise_sidestory"),
            "spin_off" => LocalizationStore.Translate("anime.labels.franchise_spinoff"),
            "summary" => LocalizationStore.Translate("anime.labels.franchise_summary"),
            "parent_story" or "full_story" => LocalizationStore.Translate("anime.labels.franchise_parent"),
            _ => string.Empty
        };
    }

    private static bool IsMangaOrNovelKind(string? kind)
    {
        if (string.IsNullOrEmpty(kind)) return false;
        var k = kind.ToLowerInvariant();
        return k is "manga" or "manhwa" or "manhua" or "novel" or "light_novel" or "one_shot" or "doujin";
    }

    private static bool IsSpecialOrShortNode(ShikiFranchiseNode node)
    {
        if (string.IsNullOrEmpty(node.Kind)) return false;
        var k = node.Kind.ToLowerInvariant();
        if (k is "special" or "music") return true;

        var nameLower = (node.Name ?? string.Empty).ToLowerInvariant();
        if (nameLower.Contains("special") || nameLower.Contains("chibi") || nameLower.Contains("mini") || nameLower.Contains("usj"))
            return true;

        if (k is "ona" && node.Weight <= 1) return true;

        return false;
    }
}
