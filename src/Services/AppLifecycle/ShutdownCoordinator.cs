using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Services.AppLifecycle.Shutdown;

namespace Kiriha.Services.AppLifecycle;

public sealed class ShutdownCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IShutdownHandler> _shutdownHandlers;
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private int _shutdownRequested;
    private int _shutdownReady;
    private int _drained;

    public ShutdownCoordinator(
        IServiceProvider serviceProvider, 
        IEnumerable<IShutdownHandler> shutdownHandlers)
    {
        _serviceProvider = serviceProvider;
        _shutdownHandlers = shutdownHandlers;
    }

    public async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (Volatile.Read(ref _shutdownReady) != 0)
            return;

        e.Cancel = true;

        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            return;

        await DrainAsync();

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
    }

    public async Task DrainAsync()
    {
        if (Volatile.Read(ref _drained) != 0)
            return;

        await _drainLock.WaitAsync();
        try
        {
            if (_drained != 0)
                return;

            foreach (var handler in _shutdownHandlers)
            {
                await handler.FlushAsync();
            }
            
            Volatile.Write(ref _drained, 1);
            Volatile.Write(ref _shutdownReady, 1);
        }
        finally
        {
            _drainLock.Release();
        }
    }
}
