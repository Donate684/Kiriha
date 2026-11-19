using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Services.Metadata;

namespace Kiriha.Utils.Graphs;

public static partial class FranchiseLayoutEngine
{
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

    private static string GetRoleBadge(ShikiFranchiseNode node, bool isMainSpine)
    {
        if (IsMangaOrNovelKind(node.Kind))
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleManga);

        if (isMainSpine)
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleMain);

        if (string.Equals(node.Kind, "movie", StringComparison.OrdinalIgnoreCase))
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleMovie);

        if (IsSpecialOrShortNode(node))
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSpecial);

        if (string.Equals(node.Kind, "ova", StringComparison.OrdinalIgnoreCase))
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSideStory);

        return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSpinOff);
    }

    private static string GetRelationLabel(string relation, string targetKind)
    {
        if (string.Equals(targetKind, "movie", StringComparison.OrdinalIgnoreCase))
            return LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleMovie);

        return relation.ToLowerInvariant() switch
        {
            "sequel" => LocalizationService.Active.GetLoc(AppConstants.Franchise.Sequel),
            "prequel" => LocalizationService.Active.GetLoc(AppConstants.Franchise.Prequel),
            "side_story" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSideStory),
            "spin_off" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSpinOff),
            "summary" => LocalizationService.Active.GetLoc(AppConstants.Franchise.Summary),
            "parent_story" or "full_story" => LocalizationService.Active.GetLoc(AppConstants.Franchise.Parent),
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
