using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Messages;

/// <summary>
/// Closed hierarchy of tracking messages published across the application.
/// </summary>
public closed record class TrackingMessage;

public record MediaChangedMessage(ParsedMedia? Media) : TrackingMessage;

public record AnimeMatchedMessage(AnimeEntity? Anime) : TrackingMessage;

public record TrackingCountdownMessage(string Countdown) : TrackingMessage;

public record TrackingStatusMessage(string Status) : TrackingMessage;
