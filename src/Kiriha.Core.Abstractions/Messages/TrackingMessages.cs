using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Messages;

/// <summary>
/// Closed hierarchy of tracking messages published across the application.
/// </summary>
[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record class TrackingMessage;

public record MediaChangedMessage(ParsedMedia? Media) : TrackingMessage;

public record AnimeMatchedMessage(AnimeEntity? Anime) : TrackingMessage;

public record TrackingCountdownMessage(string Countdown) : TrackingMessage;

public record TrackingStatusMessage(string Status) : TrackingMessage;
