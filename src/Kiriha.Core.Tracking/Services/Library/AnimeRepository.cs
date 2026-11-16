using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Collections;

namespace Kiriha.Core.Tracking.Library;

public partial class AnimeRepository : IAnimeRepository
{
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IDatabaseInitializer _dbInit;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IRecognitionCache _recognitionCache;

    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted;
    private readonly Dictionary<(int Id, MediaKind Kind), CancellationTokenSource> _recentlyDeletedIds = new();
    private readonly Lock _recentlyDeletedLock = new();
    private readonly Dictionary<(int Id, MediaKind Kind), AnimeEntity> _idIndex = new();
    private readonly Lock _indexLock = new();

    public Task InitializationTask => _initTcs.Task;
    public bool IsInitializing => Volatile.Read(ref _initStarted) == 1 && !_initTcs.Task.IsCompleted;

    private readonly Lock _snapshotLock = new();
    private IReadOnlyList<AnimeEntity>? _cachedSnapshot;

    public BulkObservableCollection<AnimeEntity> Collection { get; } = new();
    System.Collections.ObjectModel.ObservableCollection<AnimeEntity> IAnimeRepository.Collection => Collection;

    public IEnumerable<AnimeEntity> GetCollection()
    {
        return Collection;
    }

    public AnimeEntity? FindById(int animeId, MediaKind kind = MediaKind.Anime)
    {
        lock (_indexLock)
        {
            return _idIndex.GetValueOrDefault((animeId, kind));
        }
    }

    public AnimeRepository(
        IUserAnimeRepository userAnimeRepo,
        IDatabaseInitializer dbInit,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        IRecognitionCache recognitionCache)
    {
        _userAnimeRepo = userAnimeRepo;
        _dbInit = dbInit;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _recognitionCache = recognitionCache;

        Collection.CollectionChanged += (s, e) =>
        {
            lock (_snapshotLock)
            {
                _cachedSnapshot = null;
            }

            lock (_indexLock)
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    _idIndex.Clear();
                    foreach (var item in Collection)
                    {
                        _idIndex[(item.Id, item.MediaKind)] = item;
                    }
                }
                else
                {
                    if (e.OldItems != null)
                    {
                        foreach (AnimeEntity oldItem in e.OldItems)
                        {
                            _idIndex.Remove((oldItem.Id, oldItem.MediaKind));
                        }
                    }

                    if (e.NewItems != null)
                    {
                        foreach (AnimeEntity newItem in e.NewItems)
                        {
                            _idIndex[(newItem.Id, newItem.MediaKind)] = newItem;
                        }
                    }
                }
            }
        };
    }

    public IReadOnlyList<AnimeEntity>? TryGetSnapshot()
    {
        if (!_initTcs.Task.IsCompleted)
            return null;

        lock (_snapshotLock)
        {
            if (_cachedSnapshot != null)
                return _cachedSnapshot;
        }

        if (_uiDispatcher.CheckAccess())
        {
            var snap = Collection.ToList();
            lock (_snapshotLock)
            {
                _cachedSnapshot ??= snap;
                return _cachedSnapshot;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<AnimeEntity>> GetSnapshotAsync()
    {
        lock (_snapshotLock)
        {
            if (_cachedSnapshot != null) return _cachedSnapshot;
        }

        var snapshot = await _uiDispatcher.InvokeAsync(() => Collection.ToList());
        lock (_snapshotLock)
        {
            _cachedSnapshot ??= snapshot;
            return _cachedSnapshot;
        }
    }

    public async Task ApplySyncBatchAsync(List<AnimeEntity> toRemove, List<Action> uiBatch)
    {
        if (toRemove.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_indexLock)
                {
                    foreach (var item in toRemove)
                    {
                        Collection.Remove(item);
                        _idIndex.Remove((item.Id, item.MediaKind));
                    }
                }
            });
        }

        if (uiBatch.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var action in uiBatch) action();
            });
        }
    }

    public Task<List<AnimeEntity>> GetSnapshotAsync(MediaKind[] kinds)
    {
        return _uiDispatcher.InvokeAsync(() =>
            Collection.Where(x => kinds.Contains(x.MediaKind)).ToList());
    }

    public void AddToCollection(AnimeEntity item)
    {
        if (_uiDispatcher.CheckAccess())
        {
            AddToCollectionOnUiThread(item);
        }
        else
        {
            _uiDispatcher.Post(() => AddToCollectionOnUiThread(item));
        }
    }

    private void AddToCollectionOnUiThread(AnimeEntity item)
    {
        AnimeEntity? existing;
        lock (_indexLock)
        {
            _idIndex.TryGetValue((item.Id, item.MediaKind), out existing);
        }

        if (existing != null)
        {
            item.CopyTo(existing);
            return;
        }

        Collection.Add(item);
        lock (_indexLock)
        {
            _idIndex[(item.Id, item.MediaKind)] = item;
        }
    }
}
