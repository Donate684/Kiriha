using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Data.Mapping;

public static class AnimeMapper
{
    public static AnimeEntity ToViewModel(this AnimeEntity entity)
    {
        return new AnimeEntity
        {
            Id = entity.Id,
            MediaKind = entity.MediaKind,
            Chapters = entity.Chapters,
            Volumes = entity.Volumes,
            ChaptersRead = entity.ChaptersRead,
            VolumesRead = entity.VolumesRead,
            AiredSourcePriority = entity.AiredSourcePriority,
            Title = entity.Title,
            RussianTitle = entity.RussianTitle,
            Status = entity.Status,
            Progress = entity.Progress,
            TotalEpisodes = entity.TotalEpisodes,
            Score = entity.Score,
            Type = entity.Type,
            EpisodesAired = entity.EpisodesAired,
            Synopsis = entity.Synopsis,
            RussianSynopsis = entity.RussianSynopsis,
            MainPictureUrl = entity.MainPictureUrl,
            LocalPosterPath = entity.LocalPosterPath,
            Nsfw = entity.Nsfw,
            EnglishTitle = entity.EnglishTitle,
            JapaneseTitle = entity.JapaneseTitle,
            AlternativeTitles = entity.AlternativeTitles ?? new(),
            Genres = entity.Genres ?? new(),
            Studios = entity.Studios ?? new(),
            StatusDetailed = entity.StatusDetailed,
            MeanScore = entity.MeanScore,
            Popularity = entity.Popularity,
            Rank = entity.Rank,
            AiringDate = entity.AiringDate,
            StartSeason = entity.StartSeason,
            StartYear = entity.StartYear,
            Rating = entity.Rating,
            Notes = entity.Notes,
            IsRewatching = entity.IsRewatching,
            RewatchCount = entity.RewatchCount,
            DateStarted = entity.DateStarted,
            DateCompleted = entity.DateCompleted,
            BroadcastDay = entity.BroadcastDay,
            BroadcastTime = entity.BroadcastTime,
            LastEpisodeAt = entity.LastEpisodeAt,
            LastEpisodesSync = entity.LastEpisodesSync,
            NextEpisodeAt = entity.NextEpisodeAt
        };
    }

    public static AnimeEntity ToEntity(this AnimeEntity viewModel)
    {
        var entity = new AnimeEntity();
        viewModel.UpdateEntity(entity);
        return entity;
    }

    public static void UpdateEntity(this AnimeEntity viewModel, AnimeEntity entity)
    {
        entity.Id = viewModel.Id;
        entity.MediaKind = viewModel.MediaKind;
        entity.Chapters = viewModel.Chapters;
        entity.Volumes = viewModel.Volumes;
        entity.ChaptersRead = viewModel.ChaptersRead;
        entity.VolumesRead = viewModel.VolumesRead;
        entity.AiredSourcePriority = viewModel.AiredSourcePriority;
        entity.Title = viewModel.Title;
        entity.RussianTitle = viewModel.RussianTitle;
        entity.Status = viewModel.Status;
        entity.Progress = viewModel.Progress;
        entity.TotalEpisodes = viewModel.TotalEpisodes;
        entity.Score = viewModel.Score;
        entity.Type = viewModel.Type;
        entity.EpisodesAired = viewModel.EpisodesAired;
        entity.Synopsis = viewModel.Synopsis;
        entity.RussianSynopsis = viewModel.RussianSynopsis;
        entity.MainPictureUrl = viewModel.MainPictureUrl;
        entity.LocalPosterPath = viewModel.LocalPosterPath;
        entity.Nsfw = viewModel.Nsfw;
        entity.EnglishTitle = viewModel.EnglishTitle;
        entity.JapaneseTitle = viewModel.JapaneseTitle;
        entity.AlternativeTitles = viewModel.AlternativeTitles ?? new();
        entity.Genres = viewModel.Genres ?? new();
        entity.Studios = viewModel.Studios ?? new();
        entity.StatusDetailed = viewModel.StatusDetailed;
        entity.MeanScore = viewModel.MeanScore;
        entity.Popularity = viewModel.Popularity;
        entity.Rank = viewModel.Rank;
        entity.AiringDate = viewModel.AiringDate;
        entity.StartSeason = viewModel.StartSeason;
        entity.StartYear = viewModel.StartYear;
        entity.Rating = viewModel.Rating;
        entity.Notes = viewModel.Notes;
        entity.IsRewatching = viewModel.IsRewatching;
        entity.RewatchCount = viewModel.RewatchCount;
        entity.DateStarted = viewModel.DateStarted;
        entity.DateCompleted = viewModel.DateCompleted;
        entity.BroadcastDay = viewModel.BroadcastDay;
        entity.BroadcastTime = viewModel.BroadcastTime;
        entity.LastEpisodeAt = viewModel.LastEpisodeAt;
        entity.LastEpisodesSync = viewModel.LastEpisodesSync;
        entity.NextEpisodeAt = viewModel.NextEpisodeAt;
    }
}
