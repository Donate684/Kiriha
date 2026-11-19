using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Metadata;

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
    [NotifyPropertyChangedFor(nameof(HasUserScore))]
    private string? _score;

    public bool HasUserScore => !string.IsNullOrWhiteSpace(Score) && Score != "-" && Score != "0";

    public bool IsMainLine { get; set; }
    public bool IsSpecial { get; set; }
    public bool IsMangaOrNovel { get; set; }
    public string RoleBadge { get; set; } = string.Empty;

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RussianTitle) ? RussianTitle : Node.Name;

    public string SubTitle => !string.IsNullOrWhiteSpace(RussianTitle) && !string.Equals(RussianTitle, Node.Name, StringComparison.OrdinalIgnoreCase)
        ? Node.Name
        : string.Empty;

    public string KindBadge => Node.Kind?.ToLowerInvariant() switch
    {
        "tv" => "TV",
        "movie" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleMovie),
        "ova" => "OVA",
        "ona" => "ONA",
        "special" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleSpecial),
        "manga" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleManga),
        "light_novel" or "novel" => LocalizationService.Active.GetLoc(AppConstants.Franchise.RoleNovel),
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
                return LocalizationService.Active.GetLoc("anime.status.completed");
            if (UserStatus == UserAnimeStatus.Watching)
            {
                var epAbbr = LocalizationService.Active.GetLoc("anime.labels.ep_abbr");
                return TotalEpisodes.HasValue && TotalEpisodes.Value > 0
                    ? $"{UserProgress ?? 0} / {TotalEpisodes.Value} {epAbbr}"
                    : $"{UserProgress ?? 0} {epAbbr}";
            }
            if (UserStatus == UserAnimeStatus.PlanToWatch)
                return LocalizationService.Active.GetLoc("anime.status.plan_to_watch");
            if (UserStatus == UserAnimeStatus.OnHold)
                return LocalizationService.Active.GetLoc("anime.status.on_hold");
            if (UserStatus == UserAnimeStatus.Dropped)
                return LocalizationService.Active.GetLoc("anime.status.dropped");
            return LocalizationService.Active.GetLoc("anime.labels.franchise_not_in_list");
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
    public List<FranchiseGraphVisualNode> Nodes { get; set; } = [];
    public List<FranchiseGraphVisualLink> Links { get; set; } = [];
    public List<FranchiseGraphVisualNode> TimelineNodes { get; set; } = [];
    public double Width { get; set; }
    public double Height { get; set; }
}
