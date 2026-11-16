using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class HistoryShutdownHandler : IShutdownHandler
{
    private static readonly TimeSpan HistoryFlushTimeout = TimeSpan.FromSeconds(2);
    private readonly IHistoryService _historyService;

    public HistoryShutdownHandler(IHistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 2500;
        try
        {
            var operation = _historyService.FlushAsync(HistoryFlushTimeout);
            if (!await ShutdownTimeout.WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: HistoryService.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: HistoryService.FlushAsync failed");
        }
    }
}
