using Kiriha.Core.Domain.Constants;

namespace Kiriha.Core.Domain.Models.Entities;

public partial class AnimeEntity : DomainObservableObject
{
    public int Id { get; set; }
    private MediaKind _mediaKind = MediaKind.Anime;
    public MediaKind MediaKind { get => _mediaKind; set { if (SetProperty(ref _mediaKind, value)) OnPropertyChanged("Presentation"); } }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public MediaKey Key => new(Id, _mediaKind);
    private int _chapters;
    public int Chapters { get => _chapters; set { if (SetProperty(ref _chapters, value)) OnPropertyChanged("Presentation"); } }
    private int _volumes;
    public int Volumes { get => _volumes; set { if (SetProperty(ref _volumes, value)) OnPropertyChanged("Presentation"); } }
    private int _chaptersRead;
    public int ChaptersRead { get => _chaptersRead; set { if (SetProperty(ref _chaptersRead, value)) OnPropertyChanged("Presentation"); } }
    private int _volumesRead;
    public int VolumesRead { get => _volumesRead; set { if (SetProperty(ref _volumesRead, value)) OnPropertyChanged("Presentation"); } }
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
            var normalized = NormalizeScoreString(value);
            if (SetProperty(ref _score, normalized))
            {
                _cachedScoreValue = null;
                OnPropertyChanged(nameof(UserScore));
                OnPropertyChanged("Presentation");
            }
        }
    }

    /// <summary>
    /// Typed integer user rating (1..10), or <c>null</c> when unrated.
    /// Synchronized bidirectionally with <see cref="Score"/>.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public int? UserScore
    {
        get => ScoreValue > 0 ? (int)Math.Round(ScoreValue, MidpointRounding.AwayFromZero) : null;
        set => Score = value is > 0 ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
    }

    private static string NormalizeScoreString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "-";

        ReadOnlySpan<char> span = raw.AsSpan().Trim();
        int spaceIdx = span.IndexOf(' ');
        if (spaceIdx >= 0)
            span = span[..spaceIdx];

        if (span.IsEmpty || span is "-" || span is "0")
            return "-";

        double parsed = ParseScoreToDouble(span.ToString());
        return parsed <= 0 ? "-" : span.ToString();
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public double ScoreValue => _cachedScoreValue ??= ParseScoreToDouble(_score);

    public string Type { get; set; } = AppConstants.AnimeTypes.Tv;
    private int _episodesAired;
    public int EpisodesAired { get => _episodesAired; set { if (SetProperty(ref _episodesAired, value)) OnPropertyChanged("Presentation"); } }
    private string? _synopsis = string.Empty;
    public string? Synopsis { get => _synopsis; set { if (SetProperty(ref _synopsis, value)) OnPropertyChanged("Presentation"); } }
    private string? _russianSynopsis = string.Empty;
    public string? RussianSynopsis { get => _russianSynopsis; set { if (SetProperty(ref _russianSynopsis, value)) OnPropertyChanged("Presentation"); } }
    private string? _mainPictureUrl;
    public string? MainPictureUrl
    {
        get => _mainPictureUrl;
        set
        {
            if (SetProperty(ref _mainPictureUrl, value))
                OnPropertyChanged(nameof(PosterSource));
        }
    }

    public static Func<string, bool> LocalPosterValidator { get; set; } = DefaultValidateLocalPoster;

    private static bool DefaultValidateLocalPoster(string path) => !string.IsNullOrWhiteSpace(path);

    private string? _localPosterPath;
    private bool? _cachedLocalPosterValid;

    public string? LocalPosterPath
    {
        get => _localPosterPath;
        set
        {
            bool wasInvalid = _cachedLocalPosterValid == false;
            _cachedLocalPosterValid = null;
            if (SetProperty(ref _localPosterPath, value) || (wasInvalid && !string.IsNullOrEmpty(value)))
                OnPropertyChanged(nameof(PosterSource));
        }
    }

    /// <summary>
    /// Pre-evaluates local poster validity off the UI thread so subsequent binding reads of
    /// <see cref="PosterSource"/> during virtualized scrolling perform zero synchronous disk I/O.
    /// </summary>
    public void WarmPosterCache()
    {
        if (string.IsNullOrEmpty(_localPosterPath))
        {
            _cachedLocalPosterValid = false;
            return;
        }

        _cachedLocalPosterValid ??= LocalPosterValidator(_localPosterPath);
    }

    /// <summary>
    /// Returns the best available poster source: the local cached file path if it exists and is non-empty,
    /// otherwise falls back to the remote URL. Validation is memoized per path assignment to avoid UI-thread disk I/O.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PosterSource
    {
        get
        {
            if (!string.IsNullOrEmpty(_localPosterPath))
            {
                bool isValid = _cachedLocalPosterValid ??= LocalPosterValidator(_localPosterPath);
                if (isValid) return _localPosterPath;
            }
            return IsMissingPosterUrl(_mainPictureUrl) ? null : _mainPictureUrl;
        }
    }
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
    private string? _statusDetailed;
    public string? StatusDetailed { get => _statusDetailed; set { if (SetProperty(ref _statusDetailed, value)) OnPropertyChanged("Presentation"); } }
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
    private DateTime? _airingDate;
    public DateTime? AiringDate
    {
        get => _airingDate;
        set
        {
            if (SetProperty(ref _airingDate, value))
            {
                OnPropertyChanged("Presentation");
            }
        }
    }
    public string? StartSeason { get; set; }
    public int? StartYear { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? CountryOfOrigin { get; set; }
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
    private DateTime? _lastEpisodeAt;
    public DateTime? LastEpisodeAt { get => _lastEpisodeAt; set { if (SetProperty(ref _lastEpisodeAt, value)) OnPropertyChanged("Presentation"); } }
    public DateTime? LastEpisodesSync { get; set; }
    private DateTime? _nextEpisodeAt;
    public DateTime? NextEpisodeAt { get => _nextEpisodeAt; set { if (SetProperty(ref _nextEpisodeAt, value)) OnPropertyChanged("Presentation"); } }

    public static bool IsMissingPosterUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("missing_", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/missing", StringComparison.OrdinalIgnoreCase)
            || url.Contains("missing.jpg", StringComparison.OrdinalIgnoreCase)
            || url.Contains("missing.png", StringComparison.OrdinalIgnoreCase);
    }
}
