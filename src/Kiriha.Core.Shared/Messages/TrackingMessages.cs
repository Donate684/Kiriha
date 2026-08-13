using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Shared.Messages;

public record MediaChangedMessage(ParsedMedia? Media);

public record AnimeMatchedMessage(AnimeEntity? Anime);

public record TrackingCountdownMessage(string Countdown);

public record TrackingStatusMessage(string Status);
