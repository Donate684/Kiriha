using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class BackgroundTasksShutdownHandler : IShutdownHandler
{
    private readonly IBackgroundTaskSupervisor _supervisor;

    public BackgroundTasksShutdownHandler(IBackgroundTaskSupervisor supervisor)
    {
        _supervisor = supervisor;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2500;
        try
        {
            var operation = Task.Run(async () =>
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                await _supervisor.StopAsync(stopCts.Token);
            });

            if (!await WaitForAsync(operation, timeoutMs + 500))
                Log.Warning("Shutdown flush: background tasks exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: background task stop failed");
        }
    }

    private static async Task<bool> WaitForAsync(Task operation, int timeoutMs)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
