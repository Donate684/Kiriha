using System.Collections.Specialized;
using System.Threading.Channels;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

namespace Kiriha.Core.Tracking.Franchise;

public sealed partial class FranchiseService : IFranchiseService, IDisposable
{
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeRelationRepository _relationRepo;
    private readonly IShikiApiService _shikiApi;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ISettingsService? _settingsService;
    private readonly Debouncer _rebuildDebouncer;
    private readonly CancellationTokenSource _cts = new();

    private readonly Channel<AnimeEntity> _resolutionQueue = Channel.CreateBounded<AnimeEntity>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private readonly HashSet<int> _queuedOrCheckedIds = [];
    private readonly Lock _queueLock = new();

    private IReadOnlyDictionary<int, FranchiseContext> _index = new Dictionary<int, FranchiseContext>();
    private bool _isDisposed;

    public event Action? IndexRebuilt;

    public FranchiseService(
        IAnimeRepository animeRepo,
        IAnimeRelationRepository relationRepo,
        IShikiApiService shikiApi,
        IBackgroundTaskSupervisor? backgroundTasks = null,
        IUiDispatcher? uiDispatcher = null,
        ISettingsService? settingsService = null)
    {
        _animeRepo = animeRepo;
        _relationRepo = relationRepo;
        _shikiApi = shikiApi;
        _uiDispatcher = uiDispatcher;
        _settingsService = settingsService;

        _rebuildDebouncer = new Debouncer(TimeSpan.FromMilliseconds(500), () =>
        {
            _ = RebuildIndexAsync(_cts.Token);
        });

        if (_animeRepo.Collection != null)
        {
            _animeRepo.Collection.CollectionChanged += OnCollectionChanged;
        }

        if (backgroundTasks != null)
        {
            _ = backgroundTasks.Run("FranchiseService.ResolutionWorker", ct => ResolutionWorkerLoopAsync(ct), _cts.Token);
            _ = backgroundTasks.Run("FranchiseService.InitialBuild", async ct =>
            {
                try
                {
                    if (_animeRepo.InitializationTask != null)
                    {
                        await _animeRepo.InitializationTask.WaitAsync(ct).ConfigureAwait(false);
                    }
                    await RebuildIndexAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Graceful exit on shutdown
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FranchiseService: initial index build failed");
                }
            }, _cts.Token);
        }
        else
        {
            // Start resolution worker loop in background
            _ = Task.Run(() => ResolutionWorkerLoopAsync(_cts.Token), _cts.Token);

            // Initial background index build once repository initialization completes
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_animeRepo.InitializationTask != null)
                    {
                        await _animeRepo.InitializationTask.ConfigureAwait(false);
                    }
                    await RebuildIndexAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FranchiseService: initial index build failed");
                }
            }, _cts.Token);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isDisposed) return;
        _rebuildDebouncer.Invoke();
    }

    public FranchiseContext? GetFranchiseContext(int animeId)
    {
        return _index.TryGetValue(animeId, out var ctx) ? ctx : null;
    }

    public IReadOnlyDictionary<int, FranchiseContext> GetIndex() => _index;

    private void PostToUi(Action action)
    {
        if (_uiDispatcher != null)
            _uiDispatcher.Post(action);
        else
            action();
    }

    public void EnqueueForResolution(AnimeEntity item)
    {
        if (_isDisposed || item == null || item.MediaKind != MediaKind.Anime) return;
        if (item.Status == UserAnimeStatus.Completed || item.Status == UserAnimeStatus.Dropped) return;

        // Fast path: if already resolved in memory, apply immediately
        if (_index.TryGetValue(item.Id, out var existing))
        {
            if (item.Franchise != existing)
            {
                PostToUi(() => item.Franchise = existing);
            }
            return;
        }

        lock (_queueLock)
        {
            if (_queuedOrCheckedIds.Add(item.Id))
            {
                _resolutionQueue.Writer.TryWrite(item);
            }
        }
    }

    private async Task ResolutionWorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var item = await _resolutionQueue.Reader.ReadAsync(ct).ConfigureAwait(false);
                if (item == null) continue;

                // Re-check index in case it was resolved by a prior franchise response
                if (_index.TryGetValue(item.Id, out var existing))
                {
                    PostToUi(() => item.Franchise = existing);
                    continue;
                }

                // Check if we already have relation metadata stored in SQLite for this item
                var fetchedAt = await _relationRepo.GetFetchedAtAsync(item.Id, ct).ConfigureAwait(false);
                if (fetchedAt != null)
                {
                    if (_index.TryGetValue(item.Id, out var cachedCtx))
                    {
                        PostToUi(() => item.Franchise = cachedCtx);
                    }
                    continue;
                }

                // Check if we already have relations stored in SQLite for this item
                var localRelations = await _relationRepo.GetBySourceIdAsync(item.Id, ct).ConfigureAwait(false);
                if (localRelations != null && localRelations.Count > 0)
                {
                    await RebuildIndexAsync(ct).ConfigureAwait(false);
                    if (_index.TryGetValue(item.Id, out var localCtx))
                    {
                        PostToUi(() => item.Franchise = localCtx);
                    }
                    continue;
                }

                // Fetch franchise graph from Shikimori
                var data = await _shikiApi.GetFranchiseAsync(item.Id, ct).ConfigureAwait(false);
                if (data != null && data.Nodes != null && data.Links != null)
                {
                    await IngestFranchiseResponseAsync(item.Id, data, ct).ConfigureAwait(false);

                    if (_index.TryGetValue(item.Id, out var resolvedCtx))
                    {
                        PostToUi(() => item.Franchise = resolvedCtx);
                    }
                }
                else if (data != null)
                {
                    // Standalone or not found: record empty relations to avoid repeated queries
                    await _relationRepo.ReplaceAsync(item.Id, [], ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FranchiseService: resolution loop step encountered an error");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();
        _cts.Dispose();

        if (_animeRepo.Collection != null)
        {
            _animeRepo.Collection.CollectionChanged -= OnCollectionChanged;
        }
        _rebuildDebouncer.Dispose();
    }
}
