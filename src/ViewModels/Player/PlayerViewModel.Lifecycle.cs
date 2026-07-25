using System;
using Serilog;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    public void Dispose()
    {
        try { _updateTracksCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling tracks CTS"); }
        try { _updateTracksCts?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing tracks CTS"); }

        try { _updateNavigationCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling navigation CTS"); }
        try { _updateNavigationCts?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing navigation CTS"); }

        try { _timelinePreview.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing timeline preview"); }

        if (_timer != null)
        {
            _timer.Tick -= OnTimerTick;
            try { _timer.Stop(); } catch (Exception ex) { Log.Debug(ex, "Error stopping timer"); }
            _timer = null;
        }

        try { Overlay.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing overlay"); }

        _playback.FileLoaded -= OnPlayerFileLoaded;
        _playback.PlaybackEnded -= OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged -= OnPlayerPlaybackStateChanged;
        _playback.TracksChanged -= OnPlayerTracksChanged;

        try { _statePublisher.PublishClosed(); } catch (Exception ex) { Log.Debug(ex, "Error publishing closed state"); }

        try { _playback.Detach(); } catch (Exception ex) { Log.Debug(ex, "Error detaching playback"); }
        try { _statePublisher.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing state publisher"); }
    }

    private Kiriha.Models.Api.InternalPlayerState CreatePlayerState()
    {
        var titleToUse = !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
        if (string.IsNullOrEmpty(titleToUse))
            titleToUse = AnimeTitle;

        return new Kiriha.Models.Api.InternalPlayerState
        {
            AnimeId = _animeId,
            OriginalTitle = !string.IsNullOrEmpty(OriginalTitle) ? OriginalTitle : System.IO.Path.GetFileNameWithoutExtension(VideoUrl),
            AnimeTitle = titleToUse,
            Episode = RawEpisodeText,
            Position = CurrentTime,
            Duration = Duration,
            IsPlaying = IsPlaying
        };
    }
}
