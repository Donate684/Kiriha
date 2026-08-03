using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Models.Entities;

namespace Kiriha.Models;

public readonly partial struct AnimeItemPresentation
{
    public bool IsNewEpisode => _item.LastEpisodeAt.HasValue && (_now - _item.LastEpisodeAt.Value).TotalDays < 2;

    public string AiringBadgeText
    {
        get
        {
            if (_item.Status == UserAnimeStatus.Dropped) return string.Empty;

            if (IsNewEpisode && HasNewEpisodes) return UIUtils.GetLoc("anime.labels.new_ep");
            if (_item.NextEpisodeAt.HasValue)
            {
                if (_item.StatusDetailed?.Equals("finished_airing", StringComparison.OrdinalIgnoreCase) == true || _item.StatusDetailed?.Equals("finished airing", StringComparison.OrdinalIgnoreCase) == true)
                    return string.Empty;

                var diff = _item.NextEpisodeAt.Value - _now;

                if (diff.TotalSeconds <= 0)
                {
                    if (diff.TotalHours < -48) return string.Empty;
                    return UIUtils.GetLoc("anime.labels.new_ep") + "?";
                }

                if (diff.TotalDays >= 1)
                    return $"{(int)diff.TotalDays}{UIUtils.GetLoc("common.time.day_abbr")}";

                if (diff.TotalHours >= 1)
                    return $"{diff.Hours}{UIUtils.GetLoc("common.time.hour_abbr")} {diff.Minutes}{UIUtils.GetLoc("common.time.min_abbr")}";

                return $"{diff.Minutes}{UIUtils.GetLoc("common.time.min_abbr")}";
            }

            return string.Empty;
        }
    }

    public string NextEpisodeAtDisplay => _item.NextEpisodeAt?.ToLocalTime().ToString("g") ?? "-";

    public string AiringBadgeColor
    {
        get
        {
            if (IsNewEpisode && HasNewEpisodes) return "#FF4500";
            if (_item.NextEpisodeAt.HasValue && (_item.NextEpisodeAt.Value - _now).TotalSeconds <= 0) return "#FF8C00";
            return "#4CAF50";
        }
    }

    public Avalonia.Media.IBrush ProgressAccentBrush
    {
        get
        {
            var app = Avalonia.Application.Current;
            if (app != null && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out var res) && res is Avalonia.Media.Color accent)
            {
                if (IsAccentColorRedOrOrange())
                {
                    return Avalonia.Media.SolidColorBrush.Parse("#0078D7");
                }
                return new Avalonia.Media.SolidColorBrush(accent);
            }
            return Avalonia.Media.SolidColorBrush.Parse("#0078D7");
        }
    }

    private static bool IsAccentColorRedOrOrange()
    {
        var app = Avalonia.Application.Current;
        if (app != null && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out var res) && res is Avalonia.Media.Color accent)
        {
            float r = accent.R / 255f;
            float g = accent.G / 255f;
            float b = accent.B / 255f;
            
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            
            if (max == min) return false;
            
            float hue = 0f;
            if (max == r) hue = (g - b) / (max - min);
            else if (max == g) hue = 2f + (b - r) / (max - min);
            else hue = 4f + (r - g) / (max - min);
            
            hue *= 60f;
            if (hue < 0) hue += 360f;
            
            return hue <= 45f || hue >= 330f;
        }
        return false;
    }

    public string TotalPart
    {
        get
        {
            var isManga = _item.MediaKind != MediaKind.Anime;
            var totalCount = isManga ? _item.Chapters : _item.TotalEpisodes;
            var total = totalCount > 0 ? totalCount.ToString() : "?";
            if (isManga)
            {
                if (IsCompleted && !_item.IsRewatching)
                    return UIUtils.GetLoc("anime.labels.total_ch_finished", total);
                return UIUtils.GetLoc("anime.labels.total_ch_format", total);
            }
            if (IsCompleted && !_item.IsRewatching)
                return UIUtils.GetLoc("anime.labels.total_ep_finished", total);
            return UIUtils.GetLoc("anime.labels.total_ep_format", total);
        }
    }

    public string EpisodesDisplay
    {
        get
        {
            var episodes = _item.TotalEpisodes > 0 ? _item.TotalEpisodes.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_ep_finished" : "anime.labels.total_ep_format";
            var totalPart = UIUtils.GetLoc(format, episodes);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.Progress} {totalPart}";
        }
    }

    public string ChaptersDisplay
    {
        get
        {
            var chapters = _item.Chapters > 0 ? _item.Chapters.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_ch_finished" : "anime.labels.total_ch_format";
            var totalPart = UIUtils.GetLoc(format, chapters);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.ChaptersRead} {totalPart}";
        }
    }

    public string VolumesDisplay
    {
        get
        {
            var volumes = _item.Volumes > 0 ? _item.Volumes.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_vol_finished" : "anime.labels.total_vol_format";
            var totalPart = UIUtils.GetLoc(format, volumes);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.VolumesRead} {totalPart}";
        }
    }

    public string DisplayAiringStatus
    {
        get
        {
            return _item.StatusDetailed?.ToLowerInvariant() switch
            {
                "currently_airing" or "currently airing" => UIUtils.GetLoc("anime.status.currently_airing"),
                "finished_airing" or "finished airing" => UIUtils.GetLoc("anime.status.finished_airing"),
                "not_yet_aired" or "not yet aired" or "anons" => UIUtils.GetLoc("anime.status.not_yet_aired"),
                _ => _item.StatusDetailed != null ? UIUtils.GetLoc("anime.status." + _item.StatusDetailed.ToLowerInvariant().Replace(" ", "_")) : UIUtils.GetLoc("anime.status.unknown")
            };
        }
    }

    public bool ShowAiredInfo => IsAnime && !(_item.StatusDetailed?.Equals("finished_airing", StringComparison.OrdinalIgnoreCase) == true || _item.StatusDetailed?.Equals("finished airing", StringComparison.OrdinalIgnoreCase) == true);

    public bool HasGenres => _item.Genres != null && _item.Genres.Count > 0;

    public IEnumerable<string> TopGenres => _item.Genres?.Take(2) ?? Enumerable.Empty<string>();

    public bool HasStudios => _item.Studios != null && _item.Studios.Count > 0;
}
