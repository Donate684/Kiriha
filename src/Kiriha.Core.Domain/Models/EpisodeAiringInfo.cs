using System;

namespace Kiriha.Core.Domain.Models;

public sealed record EpisodeAiringInfo(
    int SourceId,
    int MalId,
    string? Status,
    int? NextEpisode,
    DateTime? NextEpisodeAt,
    int? TotalEpisodes,
    int? EpisodesAired = null);
