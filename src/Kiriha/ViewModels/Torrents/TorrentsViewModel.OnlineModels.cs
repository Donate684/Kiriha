using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Streaming.Models;

namespace Kiriha.ViewModels.Torrents;

public sealed class OnlineAnimeItem : ObservableObject
{
    public OnlineAnimeItem(StreamingSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    public StreamingSearchResult Result { get; }

    public string Id => Result.Id;
    public string Title => Result.Title;
    public string? ThumbnailUrl => Result.ThumbnailUrl;
    public string Url => Result.Url;
    public string ProviderName => Result.ProviderName;
}

public sealed class OnlineEpisodeItem : ObservableObject
{
    private readonly ILocalizer? _localizer;

    public OnlineEpisodeItem(
        StreamingEpisode episode,
        ILocalizer? localizer = null,
        bool isWatched = false,
        bool isNew = false)
    {
        ArgumentNullException.ThrowIfNull(episode);
        Episode = episode;
        _localizer = localizer;
        _isWatched = isWatched;
        _isNew = isNew;
    }

    public StreamingEpisode Episode { get; }
    public int EpisodeNumber => Episode.EpisodeNumber;
    public string RawTitle => Episode.Title;
    public string? EpisodeId => Episode.EpisodeId;

    public string FormattedNumber => EpisodeNumber < 10 ? $"0{EpisodeNumber}" : EpisodeNumber.ToString();
    public string DisplayNumber => EpisodeNumber.ToString();

    public string DisplayTitle
    {
        get
        {
            var raw = Episode.Title?.Trim();
            if (string.IsNullOrWhiteSpace(raw) || Regex.IsMatch(raw, @"^(Episode|\u0421\u0435\u0440\u0438\u044F|\u042D\u043F\u0438\u0437\u043E\u0434)?\s*\d+$", RegexOptions.IgnoreCase))
            {
                return _localizer?.GetLoc("torrents.online.episode_label", EpisodeNumber) ?? $"Episode {EpisodeNumber}";
            }

            var match = Regex.Match(raw, @"^(Episode|\u0421\u0435\u0440\u0438\u044F|\u042D\u043F\u0438\u0437\u043E\u0434)\s*\d+[:.\s-]+(?<name>.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var epPrefix = _localizer?.GetLoc("torrents.online.episode_label", EpisodeNumber) ?? $"Episode {EpisodeNumber}";
                return $"{epPrefix}: {match.Groups["name"].Value.Trim()}";
            }

            return raw;
        }
    }

    public string Title => DisplayTitle;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isWatched;
    public bool IsWatched
    {
        get => _isWatched;
        set => SetProperty(ref _isWatched, value);
    }

    private bool _isNew;
    public bool IsNew
    {
        get => _isNew;
        set => SetProperty(ref _isNew, value);
    }
}

public sealed class OnlineSourceItem : ObservableObject
{
    public OnlineSourceItem(StreamingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public StreamingSource Source { get; }
    public string Title => Source.Title;
    public string PlayerUrl => Source.PlayerUrl;
    public string? Dubber => Source.Dubber;
    public string? Provider => Source.Provider;
    public string? Quality => Source.Quality;
    public string? SubtitleUrl => Source.SubtitleUrl;

    public bool IsDirectMp4 =>
        PlayerUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
        || PlayerUrl.Contains(".mp4?", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("[AnimeLib MP4", StringComparison.OrdinalIgnoreCase)
        || Title.Contains(" MP4", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "AniLibria", StringComparison.OrdinalIgnoreCase)
        || PlayerUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

    public bool IsSubtitles =>
        !string.IsNullOrWhiteSpace(SubtitleUrl)
        || Title.Contains("Subtitles", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("(Sub)", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("[Sub]", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("[CR]", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("Subs", StringComparison.OrdinalIgnoreCase)
        || (Dubber != null && (Dubber.Contains("Subtitles", StringComparison.OrdinalIgnoreCase) || Dubber.Contains("[CR]", StringComparison.OrdinalIgnoreCase) || Dubber.Contains("Subs", StringComparison.OrdinalIgnoreCase) || Dubber.Contains("(Sub)", StringComparison.OrdinalIgnoreCase)));

    public string? QualityTag
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Source.Quality))
                return Source.Quality;
            if (Title.Contains("2160p", StringComparison.OrdinalIgnoreCase) || Title.Contains("4K", StringComparison.OrdinalIgnoreCase))
                return "4K";
            if (Title.Contains("1080p", StringComparison.OrdinalIgnoreCase) || Title.Contains("1080", StringComparison.OrdinalIgnoreCase))
                return "1080p";
            if (Title.Contains("720p", StringComparison.OrdinalIgnoreCase) || Title.Contains("720", StringComparison.OrdinalIgnoreCase))
                return "720p";
            if (Title.Contains("480p", StringComparison.OrdinalIgnoreCase) || Title.Contains("480", StringComparison.OrdinalIgnoreCase))
                return "480p";
            if (Title.Contains("360p", StringComparison.OrdinalIgnoreCase) || Title.Contains("360", StringComparison.OrdinalIgnoreCase))
                return "360p";
            return null;
        }
    }

    public bool HasQuality => !string.IsNullOrEmpty(QualityTag);
    public bool IsFhd => QualityTag != null && (QualityTag.StartsWith("1080", StringComparison.OrdinalIgnoreCase) || QualityTag.StartsWith("2160", StringComparison.OrdinalIgnoreCase) || QualityTag == "4K");
    public bool IsHd => QualityTag != null && QualityTag.StartsWith("720", StringComparison.OrdinalIgnoreCase);
    public bool Is720p => IsHd;
    public bool Is480pOrLower => QualityTag != null && (QualityTag.StartsWith("480", StringComparison.OrdinalIgnoreCase) || QualityTag.StartsWith("360", StringComparison.OrdinalIgnoreCase));

    public string CleanStudioName
    {
        get
        {
            var raw = !string.IsNullOrWhiteSpace(Dubber) ? Dubber : Title;

            if (raw.EndsWith(".Subtitles", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[..^10];
            }

            var bracketIdx = raw.IndexOf('[');
            if (bracketIdx > 0)
            {
                raw = raw[..bracketIdx];
            }

            var parenIdx = raw.IndexOf('(');
            if (parenIdx > 0)
            {
                raw = raw[..parenIdx];
            }

            var trimmed = raw.Trim();
            return !string.IsNullOrWhiteSpace(trimmed) ? trimmed : Title;
        }
    }

    public string? Language => Source.Language ?? DetectLanguage(Title, Dubber);
    public string? LanguageDisplay => Language?.ToUpperInvariant();
    public bool HasLanguage => !string.IsNullOrWhiteSpace(LanguageDisplay);

    public bool IsRussian => string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Language);
    public bool IsNonRussian => !string.IsNullOrWhiteSpace(Language) && !string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase);

    private static string? DetectLanguage(string? title, string? dubber)
    {
        var combined = $"{title} {dubber}";
        if (string.IsNullOrWhiteSpace(combined)) return null;

        if (Regex.IsMatch(combined, @"\b(ru|rus)\b|\[ru\]|\(ru\)|\[rus\]|\(rus\)|\u0440\u0443\u0441", RegexOptions.IgnoreCase))
            return "ru";

        if (Regex.IsMatch(combined, @"\b(en|eng)\b|\[en\]|\(en\)|\[eng\]|\(eng\)|english", RegexOptions.IgnoreCase))
            return "en";

        if (Regex.IsMatch(combined, @"\b(ja|jp|jpn)\b|\[ja\]|\(ja\)|\[jpn\]|\(jpn\)|japanese|raw", RegexOptions.IgnoreCase))
            return "ja";

        return null;
    }

    public string KindIcon => IsSubtitles ? "Subtitles" : (IsDirectMp4 ? "LightningBolt" : "AccountVoice");
}
