using Kiriha.Core.Domain.Models;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private bool _suppressVideoUrlResolution;

    partial void OnVideoUrlChanged(string value)
    {
        if (_suppressVideoUrlResolution)
        {
            _suppressVideoUrlResolution = false;
            OnPropertyChanged(nameof(TrackingTitle));
            return;
        }

        ResolveAndApplyMetadata(value);
    }

    internal void ResolveAndApplyMetadata(string value)
    {
        OnPropertyChanged(nameof(TrackingTitle));

        if (string.IsNullOrEmpty(value) || _isInitializing) return;

        var resolved = _metadataResolver?.Resolve(value) ?? PlayerMediaMetadata.FromVideoPath(value);

        // If the resolver didn't find an anime in DB, but we already have anime context from a previous file
        // of the same anime series in the same directory, preserve the known titles and anime ID!
        if (resolved.AnimeId is null && _animeId.HasValue && IsSameAnimeSeries(resolved, value))
        {
            resolved = resolved with
            {
                AnimeId = _animeId,
                TitleRu = AnimeTitleRu,
                TitleEn = AnimeTitleEn,
                TitleRomaji = !string.IsNullOrEmpty(AnimeTitleRomaji) ? AnimeTitleRomaji : resolved.TitleRomaji
            };
        }
        _previousVideoUrlForMetadata = value;

        ApplyMetadata(resolved, overwriteAll: true);
        UpdateNavigationAvailability();
    }

    private bool IsSameAnimeSeries(PlayerMediaMetadata newResolved, string newVideoUrl)
    {
        if (string.IsNullOrEmpty(_previousVideoUrlForMetadata) || string.IsNullOrEmpty(newVideoUrl))
            return false;

        var prevDir = System.IO.Path.GetDirectoryName(_previousVideoUrlForMetadata);
        var newDir = System.IO.Path.GetDirectoryName(newVideoUrl);
        if (string.IsNullOrEmpty(prevDir) || !string.Equals(prevDir, newDir, StringComparison.OrdinalIgnoreCase))
            return false;

        var newTitle = !string.IsNullOrWhiteSpace(newResolved.TitleRomaji)
            ? newResolved.TitleRomaji
            : (!string.IsNullOrWhiteSpace(newResolved.TitleRu) ? newResolved.TitleRu : newResolved.OriginalTitle);
        if (string.IsNullOrWhiteSpace(newTitle))
            return false;

        return IsTitleMatch(newTitle, AnimeTitleRomaji) ||
               IsTitleMatch(newTitle, AnimeTitleEn) ||
               IsTitleMatch(newTitle, AnimeTitleRu);
    }

    private static bool IsTitleMatch(string a, string? b)
    {
        if (string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        var normA = NormalizeTitle(a);
        var normB = NormalizeTitle(b);
        if (string.IsNullOrEmpty(normA) || string.IsNullOrEmpty(normB)) return false;

        return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string s)
    {
        Span<char> span = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        int pos = 0;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
                span[pos++] = c;
        }
        return new string(span[..pos]);
    }

    private void ApplyMetadata(PlayerMediaMetadata metadata, bool overwriteAll = false)
    {
        _animeId = metadata.AnimeId ?? (overwriteAll ? null : _animeId);

        if (!_animeId.HasValue && _metadataResolver != null)
        {
            var searchTitle = !string.IsNullOrWhiteSpace(metadata.TitleRu)
                ? metadata.TitleRu
                : (!string.IsNullOrWhiteSpace(metadata.TitleEn) ? metadata.TitleEn : metadata.OriginalTitle);

            if (!string.IsNullOrWhiteSpace(searchTitle))
            {
                var resolvedByTitle = _metadataResolver.ResolveByTitle(searchTitle, metadata.EpisodeText);
                if (resolvedByTitle?.AnimeId.HasValue == true)
                {
                    _animeId = resolvedByTitle.AnimeId;
                    if (string.IsNullOrWhiteSpace(metadata.TitleRu) && !string.IsNullOrWhiteSpace(resolvedByTitle.TitleRu))
                        metadata = metadata with { TitleRu = resolvedByTitle.TitleRu };
                    if (string.IsNullOrWhiteSpace(metadata.TitleEn) && !string.IsNullOrWhiteSpace(resolvedByTitle.TitleEn))
                        metadata = metadata with { TitleEn = resolvedByTitle.TitleEn };
                    if (string.IsNullOrWhiteSpace(metadata.TitleRomaji) && !string.IsNullOrWhiteSpace(resolvedByTitle.TitleRomaji))
                        metadata = metadata with { TitleRomaji = resolvedByTitle.TitleRomaji };
                }
            }
        }

        if (overwriteAll || !string.IsNullOrWhiteSpace(metadata.OriginalTitle))
            OriginalTitle = metadata.OriginalTitle;

        if (overwriteAll || !string.IsNullOrWhiteSpace(metadata.TitleRu))
            AnimeTitleRu = metadata.TitleRu;

        if (overwriteAll || !string.IsNullOrWhiteSpace(metadata.TitleEn))
            AnimeTitleEn = metadata.TitleEn;

        if (!string.IsNullOrWhiteSpace(metadata.TitleRomaji))
            AnimeTitleRomaji = metadata.TitleRomaji;
        else if (!string.IsNullOrWhiteSpace(metadata.TitleEn))
            AnimeTitleRomaji = metadata.TitleEn;
        else if (!string.IsNullOrWhiteSpace(metadata.TitleRu))
            AnimeTitleRomaji = metadata.TitleRu;
        else if (overwriteAll)
            AnimeTitleRomaji = string.Empty;

        if (!string.IsNullOrWhiteSpace(metadata.EpisodeText) || overwriteAll)
        {
            RawEpisodeText = metadata.EpisodeText;
            var epPrefix = _localizer?.GetLoc("anime.labels.episode") ?? "\u0421\u0435\u0440\u0438\u044F";
            EpisodeTitle = string.IsNullOrEmpty(metadata.EpisodeText)
                ? string.Empty
                : $"{epPrefix} {metadata.EpisodeText}";
        }

        if (!string.IsNullOrWhiteSpace(metadata.TorrentHash))
        {
            TorrentHash = metadata.TorrentHash;
            IsTorrentStream = true;
        }

        AnimeTitle = !string.IsNullOrWhiteSpace(AnimeTitleRu) ? AnimeTitleRu : AnimeTitleRomaji;

        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(EpisodeTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
        OnPropertyChanged(nameof(TrackingTitle));
        RefreshSkipSegments();
    }

    public void ApplyExternalMetadata(PlayerMediaMetadata metadata)
    {
        var prevTitle = AnimeTitle;
        var prevId = _animeId;
        var prevEp = RawEpisodeText;

        ApplyMetadata(metadata, overwriteAll: false);

        if (!string.Equals(prevTitle, AnimeTitle, StringComparison.Ordinal) ||
            prevId != _animeId ||
            !string.Equals(prevEp, RawEpisodeText, StringComparison.Ordinal))
        {
            _statePublisher.Publish();
        }
    }

    public bool MatchesOriginalTitle(string originalTitle)
    {
        if (string.IsNullOrWhiteSpace(originalTitle))
            return false;

        var current = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
        if (string.Equals(current, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(OriginalTitle) && string.Equals(OriginalTitle, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(AnimeTitleRomaji) && string.Equals(AnimeTitleRomaji, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(AnimeTitle) && string.Equals(AnimeTitle, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(AnimeTitleRu) && string.Equals(AnimeTitleRu, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(AnimeTitleEn) && string.Equals(AnimeTitleEn, originalTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsTitleMatch(current, originalTitle))
            return true;

        if (!string.IsNullOrWhiteSpace(OriginalTitle) && IsTitleMatch(OriginalTitle, originalTitle))
            return true;

        if (!string.IsNullOrWhiteSpace(AnimeTitleRomaji) && IsTitleMatch(AnimeTitleRomaji, originalTitle))
            return true;

        if (!string.IsNullOrEmpty(current) && current.Length >= 4 && originalTitle.Length >= 4)
        {
            if (current.Contains(originalTitle, StringComparison.OrdinalIgnoreCase) ||
                originalTitle.Contains(current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Window title used by external now-playing detectors. Keep the raw filename here:
    /// parsed display metadata can lose release/season/episode details that Anitomy needs.
    /// </summary>
    public string TrackingTitle
    {
        get
        {
            var filename = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
            if (!string.IsNullOrWhiteSpace(filename) && !PlayerMediaMetadata.IsStreamManifest(filename))
                return $"[KirihaPlayer] {filename}";

            var title = !string.IsNullOrEmpty(AnimeTitleRomaji)
                ? AnimeTitleRomaji
                : (!string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu);
            return $"[KirihaPlayer] {title}";
        }
    }

    public bool UseRussianTitles => _settingsService?.Current.UI.UseRussianTitles ?? false;

    public string TopTitle
    {
        get
        {
            if (UseRussianTitles && !string.IsNullOrWhiteSpace(AnimeTitleRu))
                return AnimeTitleRu;

            if (!string.IsNullOrWhiteSpace(AnimeTitleRomaji))
                return AnimeTitleRomaji;

            if (!string.IsNullOrWhiteSpace(AnimeTitleEn))
                return AnimeTitleEn;

            return !string.IsNullOrWhiteSpace(AnimeTitleRu) ? AnimeTitleRu : AnimeTitle;
        }
    }

    public string BottomTitle
    {
        get
        {
            var top = TopTitle;

            if (UseRussianTitles && !string.IsNullOrWhiteSpace(AnimeTitleRu))
            {
                if (!string.IsNullOrWhiteSpace(AnimeTitleRomaji) && !string.Equals(AnimeTitleRomaji, top, StringComparison.OrdinalIgnoreCase))
                    return AnimeTitleRomaji;

                if (!string.IsNullOrWhiteSpace(AnimeTitleEn) && !string.Equals(AnimeTitleEn, top, StringComparison.OrdinalIgnoreCase))
                    return AnimeTitleEn;

                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(AnimeTitleEn) && !string.Equals(AnimeTitleEn, top, StringComparison.OrdinalIgnoreCase))
                return AnimeTitleEn;

            if (!string.IsNullOrWhiteSpace(AnimeTitleRu) && !string.Equals(AnimeTitleRu, top, StringComparison.OrdinalIgnoreCase))
                return AnimeTitleRu;

            return string.Empty;
        }
    }

    public bool HasBottomTitle => !string.IsNullOrEmpty(BottomTitle);
    public bool HasEpisodeAndBottom => !string.IsNullOrEmpty(EpisodeTitle) && HasBottomTitle;

    partial void OnAnimeTitleRuChanged(string value)
    {
        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }

    partial void OnAnimeTitleEnChanged(string value)
    {
        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }

    partial void OnAnimeTitleRomajiChanged(string value)
    {
        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }

    partial void OnEpisodeTitleChanged(string value)
    {
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }
}
