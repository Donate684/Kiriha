using Kiriha.Services.Tracking.Integration;
using Kiriha.Services.Tracking.Feed;
using Kiriha.Services.Tracking.Core;
using Kiriha.Services.Data.Mapping;
using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Serilog;

namespace Kiriha.Services.Tracking.Core;

public partial class TrackingService
{
    public async Task ManualMapAsync(int animeId)
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Manually mapping '{Title}' to ID {Id}", media.AnimeTitle, animeId);
            _mappingService.AddMapping(media.AnimeTitle, animeId);

            // Use a temporary flag or bypass to ensure it works even if scrobbler is disabled
            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public async Task RemoveManualMappingAsync()
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Removing manual mapping for '{Title}'", media.AnimeTitle);
            _mappingService.RemoveMapping(media.AnimeTitle);

            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public async Task AddNegativeMappingAsync()
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Adding negative mapping for '{Title}'", media.AnimeTitle);
            _mappingService.AddNegativeMapping(media.AnimeTitle);

            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public bool IsManuallyMapped()
    {
        ParsedMedia? media;
        lock (_state) media = _currentMedia;
        if (media == null) return false;
        return _mappingService.IsManuallyMapped(media.AnimeTitle) ||
               _mappingService.IsManuallyMapped(media.OriginalTitle);
    }

    private async Task MatchMediaAsync(ParsedMedia media, bool forceMatch = false)
    {
        // Check if it's the same media
        ParsedMedia? prev;
        lock (_state) prev = _currentMedia;
        if (!forceMatch && prev != null && prev.AnimeTitle == media.AnimeTitle && prev.Episode == media.Episode)
        {
            bool changed = prev.IsPlaying != media.IsPlaying || prev.ProcessName != media.ProcessName;
            lock (_state) _currentMedia = media;
            if (changed)
            {
                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media)));
                _scrobbleService.UpdatePlayingState(media.IsPlaying);
            }
            return;
        }

        lock (_state)
        {
            _currentMedia = media;
            _matchedAnime = null;
        }
        _scrobbleService.CancelScrobble();

        _uiDispatcher.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media));
            WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null)); // Clear previous match UI immediately
            WeakReferenceMessenger.Default.Send(new TrackingStatusMessage(UIUtils.GetLoc("scrobbler.status.matching")));
        });

        try
        {
            // Wait for services to be ready (e.g. at app startup)
            try { await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(5000)); } catch { }

            var userList = await _uiDispatcher.InvokeAsync(() => _animeRepo.Collection.ToList());
            
            var result = await _pipeline.RunAsync(media, userList);

            if (result.NegativelyMapped)
            {
                Log.Information("TrackingService: '{Title}' is negatively mapped, skipping auto-match", media.AnimeTitle);
                return;
            }

            // Race: another media event may have arrived while we were mapping.
            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (!IsSameMedia(cur, media)) return;

            if (result.Success)
            {
                var matched = result.MatchedAnime;

                bool isValid;
                lock (_state)
                {
                    isValid = IsSameMedia(_currentMedia, media);
                    if (isValid)
                    {
                        _matchedAnime = matched;
                    }
                }

                if (!isValid) return;

                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(matched)));

                if (matched != null)
                {
                    NotifyPlayerMetadata(media, matched);

                    UpdateDiscordPresence(media, matched);

                    if (matched.Status != UserAnimeStatus.None)
                        _scrobbleService.StartScrobble(media, matched);
                }
                else
                {
                    _discordService.UpdatePresence(media.AnimeTitle, media.Episode, 0, null, null, media.Position, media.Duration, null, media.IsPlaying);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during tracking mapping");
        }
        finally
        {
            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (IsSameMedia(cur, media))
            {
                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new TrackingStatusMessage(string.Empty)));
            }
        }
    }
}
