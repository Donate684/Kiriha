using System;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntityPresentation
{
    public bool HasFranchiseBadge => (_item.Status == UserAnimeStatus.None || _item.Status == UserAnimeStatus.PlanToWatch) && _item.Franchise?.HasRelation == true;

    public string FranchiseBadgeText
    {
        get
        {
            var ctx = _item.Franchise;
            if (ctx == null) return string.Empty;

            if (ctx.UserStatus == UserAnimeStatus.Dropped)
            {
                return GetLoc("anime.labels.franchise_dropped");
            }

            return ctx.Relation switch
            {
                FranchiseRelationKind.Sequel => GetLoc("anime.labels.franchise_sequel"),
                FranchiseRelationKind.Prequel => GetLoc("anime.labels.franchise_prequel"),
                FranchiseRelationKind.SpinOff => GetLoc("anime.labels.franchise_spinoff"),
                FranchiseRelationKind.SideStory => GetLoc("anime.labels.franchise_sidestory"),
                FranchiseRelationKind.Summary => GetLoc("anime.labels.franchise_summary"),
                FranchiseRelationKind.Parent => GetLoc("anime.labels.franchise_parent"),
                _ => ctx.UserStatus switch
                {
                    UserAnimeStatus.Completed => GetLoc("anime.labels.franchise_sequel"),
                    UserAnimeStatus.Watching => GetLoc("anime.labels.franchise_watching"),
                    UserAnimeStatus.PlanToWatch => GetLoc("anime.labels.franchise_planned"),
                    _ => GetLoc("anime.labels.franchise_related")
                }
            };
        }
    }

    public string FranchiseTooltip
    {
        get
        {
            var ctx = _item.Franchise;
            if (ctx == null) return string.Empty;

            string title = !string.IsNullOrWhiteSpace(ctx.RelatedTitle) ? ctx.RelatedTitle : "...";

            if (ctx.UserStatus == UserAnimeStatus.Dropped)
            {
                return GetLoc("anime.labels.franchise_tooltip_dropped", title);
            }

            return ctx.UserStatus switch
            {
                UserAnimeStatus.Completed => GetLoc("anime.labels.franchise_tooltip_completed", title),
                UserAnimeStatus.Watching => GetLoc("anime.labels.franchise_tooltip_watching", title),
                UserAnimeStatus.PlanToWatch => GetLoc("anime.labels.franchise_tooltip_planned", title),
                _ => GetLoc("anime.labels.franchise_tooltip_related", title)
            };
        }
    }
}
