using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

public record AnimeCompletedRatingPromptMessage(AnimeEntity Anime);

public record AnimeRewatchPromptMessage(AnimeEntity Anime, int Episode);
