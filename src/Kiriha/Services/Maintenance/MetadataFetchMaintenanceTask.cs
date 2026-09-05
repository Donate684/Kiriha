using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Utils.Parsing;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class MetadataFetchMaintenanceTask : IMaintenanceTask
{
    private readonly ISettingsService _settingsService;
    private readonly IAnimeRepository _animeRepo;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IMetadataRepository _metadataRepo;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly ImageCacheService _imageCacheService;
    private readonly IUiDispatcher _uiDispatcher;

    public MetadataFetchMaintenanceTask(
        ISettingsService settingsService,
        IAnimeRepository animeRepo,
        IUserAnimeRepository userAnimeRepo,
        IMetadataRepository metadataRepo,
        ShikiMetadataService shikiMetadata,
        ImageCacheService imageCacheService,
        IUiDispatcher uiDispatcher)
    {
        _settingsService = settingsService;
        _animeRepo = animeRepo;
        _userAnimeRepo = userAnimeRepo;
        _metadataRepo = metadataRepo;
        _shikiMetadata = shikiMetadata;
        _imageCacheService = imageCacheService;
        _uiDispatcher = uiDispatcher;
    }

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    public TimeSpan Interval => _settingsService.Current.System.EnableBackgroundMetadataFetch ? TimeSpan.FromMinutes(20) : TimeSpan.FromMinutes(1);

    private const int NetworkThrottleDelayMs = 1500;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
            return;

        // Ensure in-memory anime repository is initialized
        await _animeRepo.InitializationTask.WaitAsync(ct);

        var snapshot = await _animeRepo.GetSnapshotAsync(new[] { MediaKind.Anime, MediaKind.Manga, MediaKind.LightNovel });
        if (snapshot.Count == 0)
            return;

        var itemsToProcess = new List<AnimeEntity>();
        foreach (var item in snapshot)
        {
            if (NeedsWork(item))
            {
                itemsToProcess.Add(item);
            }
        }

        if (itemsToProcess.Count == 0)
            return;

        Log.Information("MetadataFetchMaintenanceTask: Found {Count} items needing metadata or poster download.", itemsToProcess.Count);

        foreach (var item in itemsToProcess)
        {
            ct.ThrowIfCancellationRequested();

            if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                break;

            bool performedNetworkCall = false;

            try
            {
                int cacheId = GetCacheId(item.Id, item.MediaKind);

                // 1. Fetch or apply metadata (Russian title and synopsis)
                bool needsMeta = string.IsNullOrEmpty(item.RussianTitle) || string.IsNullOrEmpty(item.RussianSynopsis);
                if (needsMeta)
                {
                    var meta = await _metadataRepo.GetAsync(cacheId);

                    // If not in database metadata cache, fetch from Shikimori API
                    if (meta == null)
                    {
                        performedNetworkCall = true;
                        meta = await _shikiMetadata.GetOrFetchMetadataAsync(item.Id, null, null, item.MediaKind);
                    }

                    if (meta != null)
                    {
                        bool changed = false;
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            if (!string.IsNullOrEmpty(meta.Russian) && item.RussianTitle != meta.Russian)
                            {
                                item.RussianTitle = meta.Russian;
                                changed = true;
                            }

                            if (!string.IsNullOrEmpty(meta.Description))
                            {
                                var cleaned = AnimeStringHelper.CleanShikiDescription(meta.Description);
                                if (item.RussianSynopsis != cleaned)
                                {
                                    item.RussianSynopsis = cleaned;
                                    changed = true;
                                }
                            }

                            if (changed)
                            {
                                item.RefreshMetadata();
                            }
                        });

                        if (changed)
                        {
                            try
                            {
                                await _userAnimeRepo.UpdateMetadataAsync(item);
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "MetadataFetchMaintenanceTask: failed to persist metadata for item {Id}", item.Id);
                            }
                        }
                    }
                }

                // 2. Download poster if missing from local cache
                if (NeedsPosterDownload(item))
                {
                    performedNetworkCall = true;
                    var localPath = await _imageCacheService.GetLocalPathOrDownload(item.MainPictureUrl!, ct);
                    if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                    {
                        await _uiDispatcher.InvokeAsync(() => item.LocalPosterPath = localPath);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MetadataFetchMaintenanceTask: Error processing item {Id} ({Title})", item.Id, item.Title);
            }

            if (performedNetworkCall)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(NetworkThrottleDelayMs), ct);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private static bool NeedsWork(AnimeEntity item)
    {
        return (string.IsNullOrEmpty(item.RussianTitle) || string.IsNullOrEmpty(item.RussianSynopsis))
            || NeedsPosterDownload(item);
    }

    private static bool NeedsPosterDownload(AnimeEntity item)
    {
        if (string.IsNullOrEmpty(item.MainPictureUrl))
            return false;

        if (string.IsNullOrEmpty(item.LocalPosterPath) || !File.Exists(item.LocalPosterPath))
            return true;

        try
        {
            return new FileInfo(item.LocalPosterPath).Length == 0;
        }
        catch
        {
            return true;
        }
    }

    private static int GetCacheId(int malId, MediaKind mediaKind) =>
        mediaKind switch
        {
            MediaKind.Manga => malId | 0x40000000,
            MediaKind.LightNovel => malId | 0x20000000,
            _ => malId
        };
}
