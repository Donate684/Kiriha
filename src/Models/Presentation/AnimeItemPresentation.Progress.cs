using System;
using Kiriha.Models.Entities;

namespace Kiriha.Models;

public readonly partial struct AnimeItemPresentation
{
    public double ProgressValue
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            var progress = IsManga ? _item.ChaptersRead : _item.Progress;
            return Math.Clamp((double)progress / total * 100, 0, 100);
        }
    }

    public double ProgressValueFraction => ProgressValue / 100.0;

    public double AiredValueFraction
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            var aired = ResolvedAiredEpisodes;
            if (aired <= 0) return 0;
            return Math.Clamp((double)aired / total, 0, 1);
        }
    }

    public bool ShowAiredProgressBar
    {
        get
        {
            if (_item.Status != UserAnimeStatus.Watching || IsCompleted) return false;
            if (IsManga) return false; // Manga doesn't have an aired schedule in Kiriha yet
            var aired = ResolvedAiredEpisodes;
            if (aired <= 0) return false;
            return _item.Progress < aired;
        }
    }

    public string ProgressDisplay
    {
        get
        {
            if (IsCompleted && !_item.IsRewatching) return TotalPart;
            if (IsManga && _item.VolumesRead > 0)
                return $"{ProgressPart} {TotalPart} | {_item.VolumesRead} {Kiriha.Core.UIUtils.GetLoc("anime.labels.total_vol_format", _item.Volumes > 0 ? _item.Volumes.ToString() : "?")}";
            return $"{ProgressPart} {TotalPart}";
        }
    }

    public bool CanEditProgress => !IsCompleted || _item.IsRewatching;

    public bool IsCompleted => _item.Status == UserAnimeStatus.Completed;

    public bool ShowProgress => !IsCompleted || _item.IsRewatching;

    public bool HasNewEpisodes => _item.Status == UserAnimeStatus.Watching && _item.Progress < ResolvedAiredEpisodes;

    public int UnseenEpisodesCount
    {
        get
        {
            if (!ShowAiredProgressBar) return 0;
            return Math.Max(0, ResolvedAiredEpisodes - _item.Progress);
        }
    }

    public string ProgressPart => IsManga ? _item.ChaptersRead.ToString() : _item.Progress.ToString();

    public int EffectiveTotal
    {
        get
        {
            if (_item.MediaKind != MediaKind.Anime)
            {
                return _item.Chapters > 0 ? _item.Chapters : Math.Max(_item.ChaptersRead, 1);
            }

            if (_item.TotalEpisodes > 0) return _item.TotalEpisodes;
            int maxKnownEpisode = Math.Max(_item.Progress, _item.EpisodesAired);
            if (maxKnownEpisode <= 0) return 12;
            return maxKnownEpisode > 24 ? ((maxKnownEpisode - 1) / 12 + 1) * 12 : (maxKnownEpisode > 12 ? 24 : 12);
        }
    }

    public int ResolvedAiredEpisodes => _item.StatusDetailed == "finished_airing" && _item.TotalEpisodes > 0
        ? _item.TotalEpisodes
        : _item.EpisodesAired;
}
