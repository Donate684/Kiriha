using Kiriha.Models.Entities;
using Kiriha.Core.Constants;
using System;
using System.Collections.Generic;

namespace Kiriha.Models.Entities;

public partial class AnimeEntity : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Id { get; set; }
    private MediaKind _mediaKind = MediaKind.Anime;
    public MediaKind MediaKind { get => _mediaKind; set { if (SetProperty(ref _mediaKind, value)) OnPropertyChanged("Presentation"); } }
    public int Chapters { get; set; }
    public int Volumes { get; set; }
    public int ChaptersRead { get; set; }
    public int VolumesRead { get; set; }
    public int AiredSourcePriority { get; set; }
    private string _title = string.Empty;
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) OnPropertyChanged("Presentation"); } }
    private string? _russianTitle;
    public string? RussianTitle { get => _russianTitle; set { if (SetProperty(ref _russianTitle, value)) OnPropertyChanged("Presentation"); } }
    private UserAnimeStatus _status = UserAnimeStatus.None;
    public UserAnimeStatus Status { get => _status; set { if (SetProperty(ref _status, value)) OnPropertyChanged("Presentation"); } }
    private int _progress;
    public int Progress { get => _progress; set { if (SetProperty(ref _progress, value)) OnPropertyChanged("Presentation"); } }
    private int _totalEpisodes;
    public int TotalEpisodes { get => _totalEpisodes; set { if (SetProperty(ref _totalEpisodes, value)) OnPropertyChanged("Presentation"); } }
    public string Score { get; set; } = "-";
    public string Type { get; set; } = AppConstants.AnimeTypes.Tv;
    private int _episodesAired;
    public int EpisodesAired { get => _episodesAired; set { if (SetProperty(ref _episodesAired, value)) OnPropertyChanged("Presentation"); } }
    public string? Synopsis { get; set; } = string.Empty;
    public string? RussianSynopsis { get; set; } = string.Empty;
    public string? MainPictureUrl { get; set; }
    public string? LocalPosterPath { get; set; }
    public string? Nsfw { get; set; }
    private string? _englishTitle;
    public string? EnglishTitle { get => _englishTitle; set { if (SetProperty(ref _englishTitle, value)) OnPropertyChanged("Presentation"); } }
    private string? _japaneseTitle;
    public string? JapaneseTitle { get => _japaneseTitle; set { if (SetProperty(ref _japaneseTitle, value)) OnPropertyChanged("Presentation"); } }
    public List<string> AlternativeTitles { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> Studios { get; set; } = new();
    public string? StatusDetailed { get; set; }
    public string? MeanScore { get; set; }
    public int Popularity { get; set; }
    public int? Rank { get; set; }
    public DateTime? AiringDate { get; set; }
    public string? StartSeason { get; set; }
    public int? StartYear { get; set; }
    private string? _rating;
    public string? Rating { get => _rating; set { if (SetProperty(ref _rating, value)) OnPropertyChanged("Presentation"); } }
    public string? Notes { get; set; }
    private bool _isRewatching;
    public bool IsRewatching { get => _isRewatching; set { if (SetProperty(ref _isRewatching, value)) OnPropertyChanged("Presentation"); } }
    public int RewatchCount { get; set; }
    public DateTime? DateStarted { get; set; }
    public DateTime? DateCompleted { get; set; }
    public string? BroadcastDay { get; set; }
    public string? BroadcastTime { get; set; }
    public DateTime? LastEpisodeAt { get; set; }
    public DateTime? LastEpisodesSync { get; set; }
    public DateTime? NextEpisodeAt { get; set; }
}
