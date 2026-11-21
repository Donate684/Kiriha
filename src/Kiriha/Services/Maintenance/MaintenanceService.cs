using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Services.Maintenance;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

namespace Kiriha.Services;

/// <summary>
/// Centralized service for periodic background maintenance tasks like update checks, 
/// RSS feed polling, and episode airing synchronization.
/// </summary>
public class MaintenanceService : IDisposable
{
    private readonly IEnumerable<IMaintenanceTask> _tasks;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private int _state; // 0 = created, 1 = started, 2 = disposed

    public MaintenanceService(
        IEnumerable<IMaintenanceTask> tasks,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _tasks = tasks;
        _backgroundTasks = backgroundTasks;
        _disposeToken = _disposeCts.Token;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;

        foreach (var task in _tasks)
        {
            _backgroundTasks.Run($"MaintenanceTask.{task.GetType().Name}", ct => RunTaskLoopAsync(task, ct), _disposeToken)
                .SafeFireAndForget(task.GetType().Name);
        }

        Log.Information("MaintenanceService: Started background tasks");
    }

    private async Task RunTaskLoopAsync(IMaintenanceTask task, CancellationToken ct)
    {
        try
        {
            if (task.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(task.InitialDelay, ct);
            }

            async Task ExecuteSafeAsync()
            {
                try
                {
                    await task.ExecuteAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "MaintenanceService: Error executing {TaskName}", task.GetType().Name);
                }
            }

            await ExecuteSafeAsync();

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(task.Interval, ct);
                await ExecuteSafeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful exit on cancellation
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _state, 2) == 2) return;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}

