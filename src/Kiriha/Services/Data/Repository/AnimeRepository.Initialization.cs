using System;
using Kiriha.Services.Data.Mapping;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public partial class AnimeRepository
{
    public async Task InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _initStarted, 1, 0) != 0)
        {
            await _initTcs.Task;
            return;
        }

        try
        {
            var total = Stopwatch.StartNew();
            var stage = Stopwatch.StartNew();
            await _dbInit.InitializationTask;
            Log.Information("StartupTiming: anime repo waited for database elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            var cached = (await _userAnimeRepo.GetAllAsync()).Select(x => x.ToViewModel()).ToList();
            Log.Information(
                "StartupTiming: cached anime loaded count={Count} elapsedMs={ElapsedMs}",
                cached?.Count ?? 0,
                stage.ElapsedMilliseconds);

            stage.Restart();
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (cached != null && cached.Count > 0)
                {
                    Collection.Reset(cached);
                    _idIndex.Clear();
                    foreach (var item in cached) _idIndex[item.Id] = item;
                }
                else
                {
                    Collection.Clear();
                    _idIndex.Clear();
                }
            });

            await Task.Run(() => _recognitionCache.BuildIndex(Collection));

            Log.Information(
                "StartupTiming: cached anime applied to UI collection count={Count} elapsedMs={ElapsedMs}",
                Collection.Count,
                stage.ElapsedMilliseconds);

            Log.Information("StartupTiming: anime repo initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize AnimeRepository");
        }
        finally
        {
            _initTcs.TrySetResult();
        }
    }
}
