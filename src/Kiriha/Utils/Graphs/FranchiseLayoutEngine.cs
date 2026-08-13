using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Utils.Graphs;

public partial class FranchiseGraphVisualNode : ObservableObject
{
    public ShikiFranchiseNode Node { get; set; } = null!;
    public double X { get; set; }
    public double Y { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public bool IsCurrent { get; set; }

    public double NodeWidth { get; set; } = 260;
    public double NodeHeight { get; set; } = 110;

    [ObservableProperty]
    private string _displayImageUrl = string.Empty;

    [ObservableProperty]
    private UserAnimeStatus _userStatus = UserAnimeStatus.None;

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
}

public class FranchiseGraphLayout
{
    public List<FranchiseGraphVisualNode> Nodes { get; set; } = new();
    public List<FranchiseGraphVisualLink> Links { get; set; } = new();
    public double Width { get; set; }
    public double Height { get; set; }
}

public static class FranchiseLayoutEngine
{
    public static FranchiseGraphLayout CalculateLayout(ShikiFranchiseResponse data, double cellWidth = 340, double cellHeight = 180)
    {
        var visualNodes = new Dictionary<int, FranchiseGraphVisualNode>();
        foreach (var node in data.Nodes)
        {
            visualNodes[node.Id] = new FranchiseGraphVisualNode
            {
                Node = node,
                IsCurrent = node.Id == data.CurrentId
            };
        }

        var visualLinks = new List<FranchiseGraphVisualLink>();
        foreach (var link in data.Links)
        {
            if (visualNodes.TryGetValue(link.SourceId, out var source) &&
                visualNodes.TryGetValue(link.TargetId, out var target))
            {
                visualLinks.Add(new FranchiseGraphVisualLink
                {
                    Link = link,
                    Source = source,
                    Target = target
                });
            }
        }

        // 1. Assign GridY using BFS
        var gridY = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        int startId = data.CurrentId;
        if (!visualNodes.ContainsKey(startId) && data.Nodes.Count > 0)
        {
            startId = data.Nodes[0].Id; // Fallback
        }

        if (visualNodes.ContainsKey(startId))
        {
            gridY[startId] = 0;
            queue.Enqueue(startId);
            visited.Add(startId);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentY = gridY[current];

            // Find all connected links
            foreach (var link in visualLinks)
            {
                int neighbor = -1;
                int neighborY = currentY;

                if (link.Source.Node.Id == current)
                {
                    neighbor = link.Target.Node.Id;
                    neighborY = currentY + GetYDelta(link.Link.Relation, true);
                }
                else if (link.Target.Node.Id == current)
                {
                    neighbor = link.Source.Node.Id;
                    neighborY = currentY + GetYDelta(link.Link.Relation, false);
                }

                if (neighbor != -1 && !visited.Contains(neighbor))
                {
                    gridY[neighbor] = neighborY;
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        // If graph is disconnected (rare), process remaining nodes
        foreach (var node in visualNodes.Values)
        {
            if (!visited.Contains(node.Node.Id))
            {
                gridY[node.Node.Id] = 0; // Or calculate based on date relative to startId
            }
            node.GridY = gridY[node.Node.Id];
        }

        // 2. Assign GridX to avoid overlaps within the same GridY layer
        var layers = visualNodes.Values.GroupBy(n => n.GridY).OrderBy(g => g.Key);

        foreach (var layer in layers)
        {
            var nodesInLayer = layer.OrderByDescending(n => n.Node.Weight).ThenBy(n => n.Node.Date).ToList();

            // Try to place the most "main" node at GridX = 0
            // Find a node that has prequel/sequel connections, or just take the heaviest
            var mainNode = nodesInLayer.FirstOrDefault(n => n.IsCurrent) ?? nodesInLayer.First();
            mainNode.GridX = 0;

            int nextRightX = 1;
            int nextLeftX = -1;

            foreach (var node in nodesInLayer)
            {
                if (node == mainNode) continue;

                // Place alternating right and left
                if (Math.Abs(nextRightX) <= Math.Abs(nextLeftX))
                {
                    node.GridX = nextRightX;
                    nextRightX++;
                }
                else
                {
                    node.GridX = nextLeftX;
                    nextLeftX--;
                }
            }
        }

        // 3. Convert Grid coordinates to Canvas coordinates
        double minX = visualNodes.Values.Min(n => n.GridX) * cellWidth;
        double minY = visualNodes.Values.Min(n => n.GridY) * cellHeight;

        // Offset so all coordinates are >= 0 with some padding
        double offsetX = -minX + 80;
        double offsetY = -minY + 80;

        foreach (var node in visualNodes.Values)
        {
            node.X = node.GridX * cellWidth + offsetX;
            node.Y = node.GridY * cellHeight + offsetY;
        }

        // 4. Calculate connection paths
        foreach (var link in visualLinks)
        {
            var src = link.Source;
            var tgt = link.Target;
            
            Avalonia.Point p1, p2, c1, c2;
            double curveOffset = 60;

            if (src.GridY < tgt.GridY)
            {
                p1 = src.BottomPoint;
                p2 = tgt.TopPoint;
                c1 = new Avalonia.Point(p1.X, p1.Y + curveOffset);
                c2 = new Avalonia.Point(p2.X, p2.Y - curveOffset);
            }
            else if (src.GridY > tgt.GridY)
            {
                p1 = src.TopPoint;
                p2 = tgt.BottomPoint;
                c1 = new Avalonia.Point(p1.X, p1.Y - curveOffset);
                c2 = new Avalonia.Point(p2.X, p2.Y + curveOffset);
            }
            else
            {
                if (src.GridX < tgt.GridX)
                {
                    p1 = src.RightPoint;
                    p2 = tgt.LeftPoint;
                    c1 = new Avalonia.Point(p1.X + curveOffset, p1.Y);
                    c2 = new Avalonia.Point(p2.X - curveOffset, p2.Y);
                }
                else
                {
                    p1 = src.LeftPoint;
                    p2 = tgt.RightPoint;
                    c1 = new Avalonia.Point(p1.X - curveOffset, p1.Y);
                    c2 = new Avalonia.Point(p2.X + curveOffset, p2.Y);
                }
            }

            link.ConnectionPath = string.Format(System.Globalization.CultureInfo.InvariantCulture, "M {0},{1} C {2},{3} {4},{5} {6},{7}", p1.X, p1.Y, c1.X, c1.Y, c2.X, c2.Y, p2.X, p2.Y);
        }

        double maxX = visualNodes.Values.Max(n => n.X);
        double maxY = visualNodes.Values.Max(n => n.Y);

        return new FranchiseGraphLayout
        {
            Nodes = visualNodes.Values.ToList(),
            Links = visualLinks,
            Width = maxX + cellWidth + 80,
            Height = maxY + cellHeight + 80
        };
    }

    private static int GetYDelta(string relation, bool isSourceToTarget)
    {
        // Shikimori relations: sequel, prequel, side_story, spin_off, alternative_setting, alternative_version, full_story, summary, parent_story
        int delta = 0;

        if (relation == "sequel" || relation == "full_story")
            delta = 1;
        else if (relation == "prequel" || relation == "parent_story")
            delta = -1;

        return isSourceToTarget ? delta : -delta;
    }
}
