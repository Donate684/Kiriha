using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Mpv.UI.Services.Player;
using Serilog;

namespace Kiriha.Mpv.UI.ViewModels.Player;

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

        Overlay.SkipPromptDismissed -= OnOverlaySkipPromptDismissed;
        try { Overlay.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing overlay"); }

        if (_playback.HasPlayer && !string.IsNullOrWhiteSpace(VideoUrl))
        {
            if (RememberPlaybackPosition && !_isEofReached && Duration > 0 && CurrentTime > 0)
            {
                if (CurrentTime < Duration * 0.95 && CurrentTime >= 5)
                {
                    PlaybackResumeStore.SavePosition(VideoUrl, CurrentTime, Duration);
                    _playback.WriteWatchLaterConfig();
                }
                else
                {
                    PlaybackResumeStore.Remove(VideoUrl);
                    _playback.DeleteWatchLaterConfig();
                    _playback.SetSavePositionOnQuit(false);
                }
            }
        }

        _playback.FileLoaded -= OnPlayerFileLoaded;
        _playback.PlaybackEnded -= OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged -= OnPlayerPlaybackStateChanged;
        _playback.TracksChanged -= OnPlayerTracksChanged;
        _playback.SubtitleDelayChanged -= OnPlayerSubtitleDelayChanged;
        _playback.EofReachedChanged -= OnPlayerEofReachedChanged;
        _playback.BufferingChanged -= OnPlayerBufferingChanged;
        DisposeTorrentMonitor();

        try { _statePublisher.PublishClosed(); } catch (Exception ex) { Log.Debug(ex, "Error publishing closed state"); }

        try { _playback.Detach(); } catch (Exception ex) { Log.Debug(ex, "Error detaching playback"); }
        try { _statePublisher.MetadataReceived -= OnExternalMetadataReceived; } catch { }
        try { _statePublisher.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing state publisher"); }

        Kiriha.Infrastructure.Utils.PowerManager.AllowDisplaySleep();
    }

    private InternalPlayerState CreatePlayerState()
    {
        var titleToUse = !string.IsNullOrEmpty(AnimeTitleRomaji)
            ? AnimeTitleRomaji
            : (!string.IsNullOrEmpty(AnimeTitleEn)
                ? AnimeTitleEn
                : (!string.IsNullOrEmpty(AnimeTitleRu) ? AnimeTitleRu : AnimeTitle));

        var filename = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
        var originalTitle = !string.IsNullOrEmpty(OriginalTitle) && !PlayerMediaMetadata.IsStreamManifest(OriginalTitle)
            ? OriginalTitle
            : ((!string.IsNullOrWhiteSpace(filename) && !PlayerMediaMetadata.IsStreamUrl(VideoUrl) && !PlayerMediaMetadata.IsStreamManifest(filename))
                ? filename
                : (!string.IsNullOrEmpty(OriginalTitle) ? OriginalTitle : titleToUse));

        return new InternalPlayerState
        {
            AnimeId = _animeId,
            OriginalTitle = originalTitle,
            AnimeTitle = titleToUse,
            Episode = RawEpisodeText,
            Position = CurrentTime,
            Duration = Duration,
            IsPlaying = IsPlaying,
            IsBuffering = IsBuffering,
            IsLoading = IsLoading,
            TorrentHash = TorrentHash
        };
    }
}
