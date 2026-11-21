using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService
{
    private void OnScrobbleCountdownUpdated(object? sender, string e)
    {
        lock (_state)
            PostForMedia(_mediaVersion, () => _eventBus.Publish(new TrackingCountdownMessage(e)));
    }

    private void OnMediaCleared(object? sender, EventArgs e) => ClearMedia(external: true);

    private void ClearMedia(bool external)
    {
        lock (_state)
        {
            if (_disposed != 0 || _currentMedia is null || IsInternalMedia(_currentMedia) == external)
                return;

            long version = ++_mediaVersion;
            _currentMedia = null;
            _matchedAnime = null;
            _scrobbleService.CancelScrobble();
            PostForMedia(version, () =>
            {
                _eventBus.Publish(new MediaChangedMessage(null));
                _eventBus.Publish(new AnimeMatchedMessage(null));
            });
            _discordService.ClearPresence();
        }
    }

    // Call under _state to order file changes with the effects of accepting a match.
    private long BeginMedia(ParsedMedia media, bool showMatchingStatus)
    {
        long version = ++_mediaVersion;
        _currentMedia = media;
        _matchedAnime = null;
        _scrobbleService.CancelScrobble();
        PostForMedia(version, () =>
        {
            _eventBus.Publish(new MediaChangedMessage(media));
            _eventBus.Publish(new AnimeMatchedMessage(null));
            if (showMatchingStatus)
                _eventBus.Publish(new TrackingStatusMessage("scrobbler.status.matching"));
        });
        return version;
    }

    private bool IsCurrentMediaVersion(long version) => _disposed == 0 && version == _mediaVersion;

    private void PostForMedia(long version, Action publish) => _uiDispatcher.Post(() =>
    {
        lock (_state)
        {
            if (IsCurrentMediaVersion(version))
                publish();
        }
    });

    private static bool IsInternalMedia(ParsedMedia media) =>
        string.Equals(media.ProcessName, "KirihaInternal", StringComparison.Ordinal);

    private async void OnMediaDetected(object? sender, ParsedMedia media) =>
        await ProcessDetectedMediaAsync(media).ConfigureAwait(false);

    internal async Task ProcessDetectedMediaAsync(ParsedMedia media)
    {
        // This runs from an async void detector event, so exceptions must be handled here.
        try
        {
            if (!_settingsService.Current.System.Scrobbler.Enabled) return;
            await MatchMediaAsync(media, external: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) { Log.Error(ex, "TrackingService: OnMediaDetected failed for {Title}", media?.AnimeTitle); }
    }

    public void SetInternalMedia(InternalPlayerState state)
    {
        SetInternalMediaAsync(state).SafeFireAndForget("SetInternalMediaAsync");
    }

    public Task SetInternalMediaAsync(InternalPlayerState state)
    {
        if (state.IsClosed)
        {
            ClearMedia(external: false);
            return Task.CompletedTask;
        }

        var media = new ParsedMedia
        {
            ProcessName = "KirihaInternal",
            OriginalTitle = !string.IsNullOrEmpty(state.OriginalTitle) ? state.OriginalTitle : state.AnimeTitle,
            AnimeTitle = state.AnimeTitle,
            Episode = state.Episode,
            Position = TimeSpan.FromSeconds(state.Position),
            Duration = TimeSpan.FromSeconds(state.Duration),
            IsPlaying = state.IsPlaying && !state.IsLoading && !state.IsBuffering && !state.HasPlaybackError,
            PlaybackRequestId = state.PlaybackRequestId,
            PlaybackSourceKey = state.PlaybackSourceKey,
            TorrentHash = state.TorrentHash,
            Pid = 0
        };

        if (state.AnimeId is > 0)
        {
            string primaryTitle = !string.IsNullOrWhiteSpace(media.OriginalTitle) ? media.OriginalTitle : media.AnimeTitle;
            if (!_mappingService.IsNegativelyMapped(primaryTitle) && !_mappingService.IsManuallyMapped(primaryTitle))
                return SetMatchedInternalMedia(media, state.AnimeId.Value);
        }

        return MatchMediaAsync(media);
    }
}
