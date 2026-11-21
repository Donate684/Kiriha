using System.Diagnostics;
using Serilog;

namespace Kiriha.Core.Tracking.Library;

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
            var cached = await _userAnimeRepo.GetAllAsync();
            if (cached != null && cached.Count > 0)
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < cached.Count; i++)
                    {
                        cached[i].WarmPosterCache();
                    }
                });
            }
            Log.Information(
                "StartupTiming: cached anime loaded count={Count} elapsedMs={ElapsedMs}",
                cached?.Count ?? 0,
                stage.ElapsedMilliseconds);

            stage.Restart();
            // The collection's Reset handler publishes the ID index and entry
            // generations together before notifying later subscribers.
            await _uiDispatcher.InvokeAsync(() => Collection.Reset(cached ?? []));

            if (cached != null && cached.Count > 0)
            {
                await Task.Run(() => _recognitionCache.BuildIndex(cached));
            }
            else
            {
                _recognitionCache.Clear();
            }

            Log.Information(
                "StartupTiming: cached anime applied to UI collection count={Count} elapsedMs={ElapsedMs}",
                cached?.Count ?? 0,
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
