using System.Collections.Generic;
using Kiriha.Core.Domain.Constants;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity
{
    public AnimeEntity Clone()
    {
        var clone = (AnimeEntity)this.MemberwiseClone();
        clone._presentation = null;
        clone.AlternativeTitles = new List<string>(AlternativeTitles);
        clone.Genres = new List<string>(Genres);
        clone.Studios = new List<string>(Studios);
        return clone;
    }

    public void CopyTo(AnimeEntity target)
    {
        target.Title = Title;
        target.RussianTitle = RussianTitle;
        target.Status = Status;
        target.Progress = Progress;
        target.TotalEpisodes = TotalEpisodes;
        target.Score = Score;
        target.Type = Type;
        target.MediaKind = MediaKind;
        target.Synopsis = Synopsis;
        target.RussianSynopsis = RussianSynopsis;
        target.MainPictureUrl = MainPictureUrl;
        target.LocalPosterPath = LocalPosterPath;
        target.Nsfw = Nsfw;
        target.EnglishTitle = EnglishTitle;
        target.JapaneseTitle = JapaneseTitle;
        target.StatusDetailed = StatusDetailed;
        target.MeanScore = MeanScore;
        target.Popularity = Popularity;
        target.Rank = Rank;
        target.AiringDate = AiringDate;
        target.StartYear = StartYear;
        target.StartSeason = StartSeason;
        target.Rating = Rating;
        target.Notes = Notes;
        target.IsRewatching = IsRewatching;
        target.RewatchCount = RewatchCount;
        target.DateStarted = DateStarted;
        target.DateCompleted = DateCompleted;
        target.BroadcastDay = BroadcastDay;
        target.BroadcastTime = BroadcastTime;
        target.LastEpisodesSync = LastEpisodesSync;

        if (AiredSourcePriority >= target.AiredSourcePriority || EpisodesAired > target.EpisodesAired)
        {
            target.EpisodesAired = EpisodesAired;
            target.AiredSourcePriority = AiredSourcePriority;
            target.LastEpisodeAt = LastEpisodeAt;
        }

        if (AppConstants.AiringStatus.IsFinishedAiring(StatusDetailed)) target.NextEpisodeAt = null;
        else if (NextEpisodeAt.HasValue) target.NextEpisodeAt = NextEpisodeAt;

        target.Genres = new List<string>(Genres);
        target.Studios = new List<string>(Studios);
        target.AlternativeTitles = new List<string>(AlternativeTitles);

        target.RefreshMetadata();
    }

    /// <summary>
    /// Checks whether this entity lacks detailed metadata (such as genres, studios, detailed status, or season),
    /// which happens when it's created from shallow search results or partial stubs.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMissingDetails =>
        string.IsNullOrEmpty(StatusDetailed) ||
        (Genres.Count == 0 && Studios.Count == 0);

    /// <summary>
    /// Merges full metadata from a fully-populated entity into this instance,
    /// preserving user progress/status unless the target had none.
    /// </summary>
    public void MergeFullDetails(AnimeEntity source)
    {
        if (source is null) return;

        if (!string.IsNullOrEmpty(source.Type)) Type = source.Type;
        MediaKind = source.MediaKind;
        if (!string.IsNullOrEmpty(source.StatusDetailed)) StatusDetailed = source.StatusDetailed;
        if (source.TotalEpisodes > 0 && TotalEpisodes <= 0) TotalEpisodes = source.TotalEpisodes;
        if (!string.IsNullOrEmpty(source.MeanScore)) MeanScore = source.MeanScore;
        if (source.Popularity > 0) Popularity = source.Popularity;
        if (source.Rank > 0) Rank = source.Rank;
        if (source.AiringDate.HasValue) AiringDate = source.AiringDate;
        if (source.StartYear.HasValue) StartYear = source.StartYear;
        if (!string.IsNullOrEmpty(source.StartSeason)) StartSeason = source.StartSeason;
        if (!string.IsNullOrEmpty(source.Season)) Season = source.Season;
        if (!string.IsNullOrEmpty(source.Rating)) Rating = source.Rating;
        if (!string.IsNullOrEmpty(source.Nsfw)) Nsfw = source.Nsfw;
        if (!string.IsNullOrEmpty(source.BroadcastDay)) BroadcastDay = source.BroadcastDay;
        if (!string.IsNullOrEmpty(source.BroadcastTime)) BroadcastTime = source.BroadcastTime;

        if (!string.IsNullOrEmpty(source.Synopsis) && string.IsNullOrEmpty(Synopsis))
            Synopsis = source.Synopsis;

        if (!string.IsNullOrEmpty(source.EnglishTitle) && string.IsNullOrEmpty(EnglishTitle))
            EnglishTitle = source.EnglishTitle;

        if (!string.IsNullOrEmpty(source.JapaneseTitle) && string.IsNullOrEmpty(JapaneseTitle))
            JapaneseTitle = source.JapaneseTitle;

        if (!string.IsNullOrEmpty(source.MainPictureUrl) && string.IsNullOrEmpty(MainPictureUrl))
            MainPictureUrl = source.MainPictureUrl;

        if (source.Genres.Count > 0)
        {
            Genres.Clear();
            Genres.AddRange(source.Genres);
        }

        if (source.Studios.Count > 0)
        {
            Studios.Clear();
            Studios.AddRange(source.Studios);
        }

        if (source.AlternativeTitles.Count > 0)
        {
            foreach (var alt in source.AlternativeTitles)
            {
                if (!AlternativeTitles.Contains(alt))
                    AlternativeTitles.Add(alt);
            }
        }

        if (Status == UserAnimeStatus.None && source.Status != UserAnimeStatus.None)
            Status = source.Status;

        RefreshMetadata();
    }
}
