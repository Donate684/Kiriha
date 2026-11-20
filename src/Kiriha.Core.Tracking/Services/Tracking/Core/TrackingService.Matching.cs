using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService
{
    public async Task ManualMapAsync(int animeId)
    {
        ParsedMedia? media;
        long version;
        lock (_state)
        {
            media = _currentMedia;
            if (media is null) return;
            _manualMapInProgress = true;
            version = _mediaVersion;
        }

        try
        {
            string primaryTitle = !string.IsNullOrWhiteSpace(media.OriginalTitle) ? media.OriginalTitle : media.AnimeTitle;
            Log.Information("TrackingService: Manually mapping '{Title}' to ID {Id}", primaryTitle, animeId);

            if (!string.IsNullOrWhiteSpace(media.AnimeTitle) &&
                !string.Equals(media.AnimeTitle, primaryTitle, StringComparison.OrdinalIgnoreCase))
            {
                _mappingService.RemoveMapping(media.AnimeTitle);
            }

            _mappingService.AddMapping(primaryTitle, animeId);

            await MatchMediaAsync(media, remapVersion: version);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public async Task RemoveManualMappingAsync()
    {
        ParsedMedia? media;
        long version;
        lock (_state)
        {
            media = _currentMedia;
            if (media is null) return;
            _manualMapInProgress = true;
            version = _mediaVersion;
        }

        try
        {
            Log.Information("TrackingService: Removing manual mapping for '{Title}'", media.AnimeTitle);
            _mappingService.RemoveMapping(media.AnimeTitle);
            if (!string.IsNullOrWhiteSpace(media.OriginalTitle) &&
                !string.Equals(media.OriginalTitle, media.AnimeTitle, StringComparison.OrdinalIgnoreCase))
            {
                _mappingService.RemoveMapping(media.OriginalTitle);
            }

            await MatchMediaAsync(media, remapVersion: version);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public async Task AddNegativeMappingAsync()
    {
        ParsedMedia? media;
        long version;
        lock (_state)
        {
            media = _currentMedia;
            if (media is null) return;
            _manualMapInProgress = true;
            version = _mediaVersion;
        }

        try
        {
            string primaryTitle = !string.IsNullOrWhiteSpace(media.OriginalTitle) ? media.OriginalTitle : media.AnimeTitle;
            Log.Information("TrackingService: Adding negative mapping for '{Title}'", primaryTitle);
            _mappingService.AddNegativeMapping(primaryTitle);

            await MatchMediaAsync(media, remapVersion: version);
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
        if (media is null) return false;
        string primaryTitle = !string.IsNullOrWhiteSpace(media.OriginalTitle) ? media.OriginalTitle : media.AnimeTitle;
        return _mappingService.IsManuallyMapped(primaryTitle);
    }

    private async Task MatchMediaAsync(ParsedMedia media, bool external = false, long? remapVersion = null)
    {
        long version;
        lock (_state)
        {
            if (_disposed != 0 ||
                (external && (_manualMapInProgress || (_currentMedia is { } current && IsInternalMedia(current)))))
                return;

            if (remapVersion is { } requestedVersion)
            {
                if (!IsCurrentMediaVersion(requestedVersion) || _currentMedia is null)
                    return;
                media = _currentMedia;
            }
            else if (_currentMedia is { } previous && IsSameMedia(previous, media))
            {
                HandleSameMediaUpdate(previous, media);
                return;
            }

            version = BeginMedia(media, showMatchingStatus: true);
        }

        try
        {
            // Wait for services to be ready (e.g. at app startup)
            const int repoInitTimeoutMs = 5000;
            try { await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(repoInitTimeoutMs)); } catch (Exception ex) when (ex is not OperationCanceledException) { }

            var userList = await _animeRepo.GetSnapshotAsync();

            var result = await _pipeline.RunAsync(media, userList);

            switch (result)
            {
                case MediaNegativelyMapped:
                    Log.Information("TrackingService: '{Title}' is negatively mapped, skipping auto-match", media.AnimeTitle);
                    ClearPlayerMetadataIfCurrent(version);
                    return;

                case MediaMatchSuccess success:
                    ApplyMatchedMedia(version, success.MatchedAnime);
                    break;

                case MediaMatchNotFound:
                    if (remapVersion.HasValue)
                        ClearPlayerMetadataIfCurrent(version);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during tracking mapping");
        }
        finally
        {
            PostForMedia(version, () => _eventBus.Publish(new TrackingStatusMessage(string.Empty)));
        }
    }

    private void ClearPlayerMetadataIfCurrent(long version)
    {
        lock (_state)
        {
            if (IsCurrentMediaVersion(version) && _currentMedia is { } current)
                ClearPlayerMetadata(current);
        }
    }
}
