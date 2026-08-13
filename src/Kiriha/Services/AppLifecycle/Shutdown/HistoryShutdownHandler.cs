using System;
using System.Threading.Tasks;
using Kiriha.Services.Data.Core;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class HistoryShutdownHandler : IShutdownHandler
{
    private readonly HistoryService _historyService;

    public HistoryShutdownHandler(HistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2500;
        try
        {
            var operation = _historyService.FlushAsync(TimeSpan.FromSeconds(2));
            if (!await WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: HistoryService.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: HistoryService.FlushAsync failed");
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
