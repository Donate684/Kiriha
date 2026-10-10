using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class TorrServerShutdownHandler : IShutdownHandler
{
    private readonly ITorrServerService _torrServer;

    public TorrServerShutdownHandler(ITorrServerService torrServer)
    {
        _torrServer = torrServer;
    }

    public Task FlushAsync()
    {
        _torrServer.Stop();
        return Task.CompletedTask;
    }
}
