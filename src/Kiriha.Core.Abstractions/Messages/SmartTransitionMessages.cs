using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

public sealed record AnimeCompletedRatingPromptMessage(AnimeEntity Anime)
{
    public LibraryEntryTarget? Target { get; init; }
    public string ExpectedScore { get; } = Anime.Score;
    public int? SuggestedScore { get; } = Anime.UserScore;
}

public sealed record AnimeRewatchPromptMessage(AnimeEntity Anime, int Episode)
{
    public LibraryEntryTarget? Target { get; init; }
    public int ExpectedRewatchCount { get; } = Anime.RewatchCount;
}
