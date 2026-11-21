using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class PlayerResidentShutdownHandler : IShutdownHandler
{
    private readonly Func<Task<PlayerShutdownResult>> _stop;

    public PlayerResidentShutdownHandler() : this(() => PlayerProcessBridge.StopResidentAsync())
    {
    }

    internal PlayerResidentShutdownHandler(Func<Task<PlayerShutdownResult>> stop) => _stop = stop;

    public async Task FlushAsync() => await TryFlushAsync();

    public async Task<bool> TryFlushAsync()
    {
        try
        {
            var result = await _stop();
            if (result is PlayerShutdownResult.Unconfirmed or PlayerShutdownResult.Failed)
                Log.Warning("Shutdown: player cleanup result was {Result}", result);
            return result is PlayerShutdownResult.Completed or PlayerShutdownResult.Unavailable;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown: failed to stop player resident process");
            return false;
        }
    }
}
