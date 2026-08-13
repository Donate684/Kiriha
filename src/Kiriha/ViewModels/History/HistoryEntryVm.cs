using System;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core;

namespace Kiriha.ViewModels.History;

/// <summary>
/// Display entry for history. Represents either a single HistoryItem or a
/// merged range of consecutive episode-watches for the same anime on the same day.
/// </summary>
public class HistoryEntryVm
{
    private readonly Kiriha.Core.Abstractions.Services.ILocalizer _localizer;

    public HistoryEntryVm(Kiriha.Core.Abstractions.Services.ILocalizer localizer)
    {
        _localizer = localizer;
    }
    public int AnimeId { get; set; }
    public string AnimeTitle { get; set; } = string.Empty;
    public string? RussianTitle { get; set; }
    public string? PosterUrl { get; set; }
    public int ActionType { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }            // Most recent action in the group (for time display)
    public int EpisodeFrom { get; set; }
    public int EpisodeTo { get; set; }
    public int Count { get; set; } = 1;                // How many raw entries were merged
    public HistoryItem? Primary { get; set; }          // Representative raw item (for OpenDetails command)

    public bool IsRange => EpisodeFrom != EpisodeTo;
    public string EpisodeLabel =>
        (ActionType == 1 || ActionType == 4 || ActionType == 6)
            ? (IsRange
                ? _localizer.GetLoc("history.episode_range", EpisodeFrom, EpisodeTo)
                : (EpisodeFrom > 0 ? _localizer.GetLoc("history.episode_single", EpisodeFrom) : string.Empty))
            : string.Empty;
}
