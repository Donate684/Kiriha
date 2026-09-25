namespace Kiriha.Core.Domain.Models.Entities;

public enum FranchiseRelationKind
{
    None = 0,
    Sequel,
    Prequel,
    SpinOff,
    SideStory,
    Summary,
    Parent,
    Other
}

public sealed class FranchiseContext
{
    public FranchiseRelationKind Relation { get; init; } = FranchiseRelationKind.None;
    public UserAnimeStatus UserStatus { get; init; } = UserAnimeStatus.None;
    public int RelatedAnimeId { get; init; }
    public string RelatedTitle { get; init; } = string.Empty;
    public int? RelatedProgress { get; init; }
    public int? RelatedTotalEpisodes { get; init; }
    public string? RelatedScore { get; init; }

    public bool HasRelation => Relation != FranchiseRelationKind.None && UserStatus != UserAnimeStatus.None;
}
