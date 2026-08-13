using Kiriha.Models.Entities;
using Kiriha.Core.Models;

namespace Kiriha.Core.Messages;

public record MediaChangedMessage(ParsedMedia? Media);

public record AnimeMatchedMessage(AnimeEntity? Anime);

public record TrackingCountdownMessage(string Countdown);

public record TrackingStatusMessage(string Status);
