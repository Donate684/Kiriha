using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class TorrServerShutdownHandler : IShutdownHandler
{
    private readonly ITorrServerService _torrServer;

    public TorrServerShutdownHandler(ITorrServerService torrServer)
    {
        _torrServer = torrServer;
    }

    public async Task FlushAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await _torrServer.FlushTorrentReleasesAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not finish releasing TorrServer torrents before shutdown");
        }
        finally
        {
            _torrServer.Stop();
        }
    }
}
