using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

public sealed record MediaChangedMessage(ParsedMedia? Media);

public sealed record AnimeMatchedMessage(AnimeEntity? Anime);

public sealed record AnimeProgressChangedMessage(AnimeEntity Anime, int Progress);

public sealed record TrackingCountdownMessage(string Countdown);

public sealed record TrackingStatusMessage(string Status);
