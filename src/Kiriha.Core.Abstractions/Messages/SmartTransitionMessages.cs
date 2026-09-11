using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

/// <summary>
/// Closed hierarchy of smart transition prompt messages.
/// </summary>
public closed record class SmartTransitionMessage;

public record AnimeCompletedRatingPromptMessage(AnimeEntity Anime) : SmartTransitionMessage;

public record AnimeRewatchPromptMessage(AnimeEntity Anime, int Episode) : SmartTransitionMessage;
