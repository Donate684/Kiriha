using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

public sealed record AnimeCompletedRatingPromptMessage(AnimeEntity Anime);

public sealed record AnimeRewatchPromptMessage(AnimeEntity Anime, int Episode);
