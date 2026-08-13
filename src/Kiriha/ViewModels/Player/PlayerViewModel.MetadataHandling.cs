using System;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnVideoUrlChanged(string value)
    {
        OnPropertyChanged(nameof(TrackingTitle));

        if (string.IsNullOrEmpty(value) || _isInitializing) return;

        ApplyMetadata(_metadataResolver?.Resolve(value) ?? PlayerMediaMetadata.FromVideoPath(value));
        UpdateNavigationAvailability();
    }

    private void ApplyMetadata(PlayerMediaMetadata metadata)
    {
        _animeId = metadata.AnimeId;
        OriginalTitle = metadata.OriginalTitle;
        AnimeTitleRu = metadata.TitleRu;
        AnimeTitleEn = metadata.TitleEn;
        RawEpisodeText = metadata.EpisodeText;
        EpisodeTitle = string.IsNullOrEmpty(metadata.EpisodeText)
            ? string.Empty
            : $"\u0421\u0435\u0440\u0438\u044F {metadata.EpisodeText}";
        AnimeTitle = AnimeTitleRu;
        OnPropertyChanged(nameof(TrackingTitle));
    }

    public void ApplyExternalMetadata(PlayerMediaMetadata metadata)
    {
        ApplyMetadata(metadata);
        _statePublisher.Publish();
    }

    public bool MatchesOriginalTitle(string originalTitle)
    {
        if (string.IsNullOrWhiteSpace(originalTitle))
            return true;

        var current = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
        return string.Equals(current, originalTitle, StringComparison.OrdinalIgnoreCase);
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
            if (!string.IsNullOrWhiteSpace(filename))
                return $"[KirihaPlayer] {filename}";

            var title = !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
            return $"[KirihaPlayer] {title}";
        }
    }

    public string TopTitle
    {
        get
        {
            return !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
        }
    }

    public string BottomTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(AnimeTitleEn) && AnimeTitleEn != AnimeTitleRu)
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
    partial void OnEpisodeTitleChanged(string value)
    {
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }
}
