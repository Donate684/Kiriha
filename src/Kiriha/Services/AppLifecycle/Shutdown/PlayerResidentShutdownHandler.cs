using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class PlayerResidentShutdownHandler : IShutdownHandler
{
    public async Task FlushAsync()
    {
        const int timeoutMs = 700;
        try
        {
            var task = PlayerProcessBridge.StopResidentAsync();
            if (!await ShutdownTimeout.WaitForAsync(task, timeoutMs))
                Log.Warning("Shutdown: stopping player resident process exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown: failed to stop player resident process");
        }
    }
}
