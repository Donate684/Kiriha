using System;

namespace Kiriha.Core.Domain.Models;

public sealed record AniListAiringInfo(
    int AniListId,
    int MalId,
    string? Status,
    int? NextEpisode,
    DateTime? NextEpisodeAt,
    int? TotalEpisodes);
