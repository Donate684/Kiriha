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
        target.Chapters = Chapters;
        target.Volumes = Volumes;
        target.ChaptersRead = ChaptersRead;
        target.VolumesRead = VolumesRead;
        target.Synopsis = Synopsis;
        target.RussianSynopsis = RussianSynopsis;
        if (!string.IsNullOrEmpty(MainPictureUrl) && !IsMissingPosterUrl(MainPictureUrl))
        {
            target.MainPictureUrl = MainPictureUrl;
            if (!string.IsNullOrEmpty(LocalPosterPath))
            {
                target.LocalPosterPath = LocalPosterPath;
            }
        }
        else if (IsMissingPosterUrl(target.MainPictureUrl))
        {
            target.MainPictureUrl = null;
            target.LocalPosterPath = null;
        }
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

        if (!string.IsNullOrEmpty(source.MainPictureUrl) && !IsMissingPosterUrl(source.MainPictureUrl) && (string.IsNullOrEmpty(MainPictureUrl) || IsMissingPosterUrl(MainPictureUrl)))
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

    /// <summary>
    /// Preserves user-specific tracking data (progress, score, notes, rewatch, start/finish dates)
    /// from an existing tracked entity if this instance has empty/default values for them.
    /// Guards against wiping out user state when performing quick-add or shallow card actions.
    /// </summary>
    public void PreserveUserFieldsFrom(AnimeEntity existing)
    {
        if (existing is null || ReferenceEquals(this, existing)) return;

        // Score: keep existing if current is empty or '-'
        if ((string.IsNullOrWhiteSpace(Score) || Score == "-") && !string.IsNullOrWhiteSpace(existing.Score) && existing.Score != "-")
        {
            Score = existing.Score;
        }

        // Notes: keep existing if current is null or empty
        if (string.IsNullOrWhiteSpace(Notes) && !string.IsNullOrWhiteSpace(existing.Notes))
        {
            Notes = existing.Notes;
        }

        // Rewatching state
        if (!IsRewatching && existing.IsRewatching)
        {
            IsRewatching = existing.IsRewatching;
        }
        if (RewatchCount == 0 && existing.RewatchCount > 0)
        {
            RewatchCount = existing.RewatchCount;
        }

        // Dates
        if (!DateStarted.HasValue && existing.DateStarted.HasValue)
        {
            DateStarted = existing.DateStarted;
        }
        if (!DateCompleted.HasValue && existing.DateCompleted.HasValue)
        {
            DateCompleted = existing.DateCompleted;
        }

        // Progress: only keep existing if current is 0
        if (Progress == 0 && existing.Progress > 0)
        {
            Progress = existing.Progress;
        }
        if (ChaptersRead == 0 && existing.ChaptersRead > 0)
        {
            ChaptersRead = existing.ChaptersRead;
        }
        if (VolumesRead == 0 && existing.VolumesRead > 0)
        {
            VolumesRead = existing.VolumesRead;
        }

        // Local poster path
        if (string.IsNullOrEmpty(LocalPosterPath) && !string.IsNullOrEmpty(existing.LocalPosterPath))
        {
            LocalPosterPath = existing.LocalPosterPath;
        }
    }
}
