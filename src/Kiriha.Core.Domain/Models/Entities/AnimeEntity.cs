using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Constants;
using System;
using System.Collections.Generic;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity : DomainObservableObject
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
    private string _score = "-";
    private double? _cachedScoreValue;
    public string Score
    {
        get => _score;
        set
        {
            if (SetProperty(ref _score, value))
            {
                _cachedScoreValue = null;
                OnPropertyChanged("Presentation");
            }
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScoreValue => _cachedScoreValue ??= ParseScoreToDouble(_score);

    public string Type { get; set; } = AppConstants.AnimeTypes.Tv;
    private int _episodesAired;
    public int EpisodesAired { get => _episodesAired; set { if (SetProperty(ref _episodesAired, value)) OnPropertyChanged("Presentation"); } }
    public string? Synopsis { get; set; } = string.Empty;
    public string? RussianSynopsis { get; set; } = string.Empty;
    public string? MainPictureUrl { get; set; }
    public string? LocalPosterPath { get; set; }
    private string? _nsfw;
    public string? Nsfw
    {
        get => _nsfw;
        set
        {
            if (SetProperty(ref _nsfw, value))
            {
                _cachedIsNsfw = null;
                OnPropertyChanged("Presentation");
            }
        }
    }
    private string? _englishTitle;
    public string? EnglishTitle { get => _englishTitle; set { if (SetProperty(ref _englishTitle, value)) OnPropertyChanged("Presentation"); } }
    private string? _japaneseTitle;
    public string? JapaneseTitle { get => _japaneseTitle; set { if (SetProperty(ref _japaneseTitle, value)) OnPropertyChanged("Presentation"); } }
    public List<string> AlternativeTitles { get; set; } = new();
    private List<string> _genres = new();
    public List<string> Genres
    {
        get => _genres;
        set
        {
            if (SetProperty(ref _genres, value))
            {
                _cachedIsNsfw = null;
                OnPropertyChanged("Presentation");
            }
        }
    }
    public List<string> Studios { get; set; } = new();
    public string? StatusDetailed { get; set; }
    private string? _meanScore;
    private double? _cachedMeanScoreValue;
    public string? MeanScore
    {
        get => _meanScore;
        set
        {
            if (SetProperty(ref _meanScore, value))
            {
                _cachedMeanScoreValue = null;
            }
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public double MeanScoreValue => _cachedMeanScoreValue ??= ParseScoreToDouble(_meanScore);

    public static double ParseScoreToDouble(string? score)
    {
        if (string.IsNullOrWhiteSpace(score) || score == "-") return 0.0;
        ReadOnlySpan<char> span = score.AsSpan().Trim();
        int spaceIdx = span.IndexOf(' ');
        if (spaceIdx >= 0) span = span[..spaceIdx];

        Span<char> buffer = stackalloc char[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            buffer[i] = span[i] == ',' ? '.' : span[i];
        }

        if (double.TryParse(buffer, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
            return val;
        return 0.0;
    }
    public int Popularity { get; set; }
    public int? Rank { get; set; }
    public DateTime? AiringDate { get; set; }
    public string? StartSeason { get; set; }
    public int? StartYear { get; set; }
    private string? _rating;
    public string? Rating
    {
        get => _rating;
        set
        {
            if (SetProperty(ref _rating, value))
            {
                _cachedIsNsfw = null;
                OnPropertyChanged("Presentation");
            }
        }
    }

    private bool? _cachedIsNsfw;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNsfw => _cachedIsNsfw ??= ComputeIsNsfw();

    private bool ComputeIsNsfw()
    {
        if (string.Equals(_rating, "rx", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(_nsfw, "black", StringComparison.OrdinalIgnoreCase)) return true;
        if (_genres != null)
        {
            for (int i = 0; i < _genres.Count; i++)
            {
                if (string.Equals(_genres[i], "Hentai", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
    public string? Notes { get; set; }
    private bool _isRewatching;
    public bool IsRewatching { get => _isRewatching; set { if (SetProperty(ref _isRewatching, value)) OnPropertyChanged("Presentation"); } }
    public int RewatchCount { get; set; }
    private DateTime? _dateStarted;
    public DateTime? DateStarted { get => _dateStarted; set => SetProperty(ref _dateStarted, value); }
    private DateTime? _dateCompleted;
    public DateTime? DateCompleted { get => _dateCompleted; set => SetProperty(ref _dateCompleted, value); }
    public string? BroadcastDay { get; set; }
    public string? BroadcastTime { get; set; }
    public DateTime? LastEpisodeAt { get; set; }
    public DateTime? LastEpisodesSync { get; set; }
    public DateTime? NextEpisodeAt { get; set; }
}
