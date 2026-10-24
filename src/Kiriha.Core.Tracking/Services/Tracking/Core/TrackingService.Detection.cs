using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService
{
    private void OnScrobbleCountdownUpdated(object? sender, string e)
    {
        _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new TrackingCountdownMessage(e)));
    }

    private void OnMediaCleared(object? sender, EventArgs e)
    {
        lock (_state)
        {
            // If internal player is currently active, external detector (Anisthesia)
            // must NOT clear the media or cancel the scrobble!
            if (ReferenceEquals(sender, _anisthesiaService) &&
                _currentMedia != null &&
                string.Equals(_currentMedia.ProcessName, "KirihaInternal", StringComparison.Ordinal))
            {
                return;
            }

            _currentMedia = null;
            _matchedAnime = null;
        }
        _scrobbleService.CancelScrobble();

        _uiDispatcher.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(new MediaChangedMessage(null));
            WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null));
        });
        _discordService.ClearPresence();
    }

    private async void OnMediaDetected(object? sender, ParsedMedia media)
    {
        // async void event handler: any leaked exception bubbles to the
        // synchronization context (here: process-wide) and tears the app down.
        // The handler is invoked from AnisthesiaService's polling thread, so
        // we can't rely on a UI-thread safety net either.
        try
        {
            if (!_settingsService.Current.System.Scrobbler.Enabled) return;

            lock (_state)
            {
                if (_manualMapInProgress) return;

                // If internal player is currently active, do not allow external detector events to override it
                if (_currentMedia != null &&
                    string.Equals(_currentMedia.ProcessName, "KirihaInternal", StringComparison.Ordinal))
                {
                    return;
                }
            }

            await MatchMediaAsync(media).ConfigureAwait(false);
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
            ParsedMedia? current;
            lock (_state) current = _currentMedia;
            if (current != null && current.ProcessName == "KirihaInternal")
            {
                OnMediaCleared(this, EventArgs.Empty);
            }
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
            IsPlaying = state.IsPlaying,
            TorrentHash = state.TorrentHash,
            Pid = 0
        };

        if (state.AnimeId is > 0)
        {
            if (_mappingService.IsNegativelyMapped(media.OriginalTitle) ||
                _mappingService.IsNegativelyMapped(media.AnimeTitle))
            {
                ClearPlayerMetadata(media);
                return Task.CompletedTask;
            }

            ParsedMedia? prev;
            Kiriha.Core.Domain.Models.Entities.AnimeEntity? existingMatched;
            lock (_state)
            {
                prev = _currentMedia;
                existingMatched = _matchedAnime;
            }

            if (prev != null && IsSameMedia(prev, media))
            {
                if (existingMatched is null)
                {
                    HandleSameMediaUpdate(prev, media);
                    return Task.CompletedTask;
                }

                if (existingMatched.Id != state.AnimeId.Value)
                {
                    return SetMatchedInternalMedia(media, existingMatched.Id);
                }
            }
            else if (_mappingService.IsManuallyMapped(media.OriginalTitle) ||
                     _mappingService.IsManuallyMapped(media.AnimeTitle))
            {
                return MatchMediaAsync(media);
            }

            return SetMatchedInternalMedia(media, state.AnimeId.Value);
        }
        else
        {
            return MatchMediaAsync(media);
        }
    }
}
