using System;
using System.Threading.Tasks;
using Kiriha.Core.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class PlayerResidentShutdownHandler : IShutdownHandler
{
    public async Task FlushAsync()
    {
        try
        {
            var task = PlayerProcessBridge.StopResidentAsync();
            if (!await WaitForAsync(task, 700))
                Log.Warning("Shutdown: stopping player resident process exceeded {Ms}ms timeout", 700);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown: failed to stop player resident process");
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
