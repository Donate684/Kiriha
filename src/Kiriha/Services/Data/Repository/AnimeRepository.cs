using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Utils.Collections;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public partial class AnimeRepository : IAnimeRepository
{
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly DatabaseInitializer _dbInit;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecognitionCache _recognitionCache;

    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted;
    private readonly Dictionary<int, CancellationTokenSource> _recentlyDeletedIds = new();
    private readonly Dictionary<int, AnimeEntity> _idIndex = new();

    public Task InitializationTask => _initTcs.Task;
    public bool IsInitializing => Volatile.Read(ref _initStarted) == 1 && !_initTcs.Task.IsCompleted;

    public BulkObservableCollection<AnimeEntity> Collection { get; } = new();
    System.Collections.ObjectModel.ObservableCollection<AnimeEntity> Kiriha.Core.Abstractions.Repositories.IAnimeRepository.Collection => Collection;

    public IEnumerable<AnimeEntity> GetCollection()
    {
        return Collection;
    }

    public AnimeRepository(
        IUserAnimeRepository userAnimeRepo,
        DatabaseInitializer dbInit,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        RecognitionCache recognitionCache)
    {
        _userAnimeRepo = userAnimeRepo;
        _dbInit = dbInit;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _recognitionCache = recognitionCache;
    }

    public async Task ApplySyncBatchAsync(List<AnimeEntity> toRemove, List<Action> uiBatch)
    {
        if (toRemove.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var item in toRemove)
                {
                    Collection.Remove(item);
                    _idIndex.Remove(item.Id);
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

    public Task<Dictionary<int, AnimeEntity>> GetExistingMapAsync(MediaKind[] kinds)
    {
        return _uiDispatcher.InvokeAsync(() =>
            Collection.Where(x => kinds.Contains(x.MediaKind)).ToDictionary(x => x.Id));
    }

    public Task<List<AnimeEntity>> GetSnapshotAsync(MediaKind[] kinds)
    {
        return _uiDispatcher.InvokeAsync(() =>
            Collection.Where(x => kinds.Contains(x.MediaKind)).ToList());
    }

    public void AddToCollection(AnimeEntity item)
    {
        Collection.Add(item);
        _idIndex[item.Id] = item;
    }
}
