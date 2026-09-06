using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;

using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

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
            if (HandleSameMediaUpdate(prev, media)) return;
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
            WeakReferenceMessenger.Default.Send(new TrackingStatusMessage("scrobbler.status.matching"));
        });

        try
        {
            // Wait for services to be ready (e.g. at app startup)
            const int repoInitTimeoutMs = 5000;
            try { await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(repoInitTimeoutMs)); } catch (Exception ex) when (ex is not OperationCanceledException) { }

            var userList = await _animeRepo.GetSnapshotAsync();

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
                ApplyMatchedMedia(media, result.MatchedAnime);
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
