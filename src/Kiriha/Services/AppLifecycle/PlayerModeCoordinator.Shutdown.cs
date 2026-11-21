using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Views.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    private PlayerShutdownSequence? _shutdown;
    private bool _remoteShutdown;
    private bool _finishedShutdown;

    private async Task<bool> PrepareRemoteShutdownAsync()
    {
        var preparation = await Dispatcher.UIThread.InvokeAsync<Task<bool>>(() =>
        {
            _remoteShutdown = true;
            return _shutdown?.PrepareAsync() ?? Task.FromResult(false);
        });
        return await preparation.ConfigureAwait(false);
    }

    private void OnPlayerShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_finishedShutdown)
            return;

        e.Cancel = true;
        _ = CompleteLocalShutdownAsync();
    }

    private async Task CompleteLocalShutdownAsync()
    {
        if (_shutdown != null)
            await _shutdown.PrepareAsync();

        if (!_remoteShutdown)
            FinishPlayerShutdown();
    }

    private void OnPlayerWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not PlayerWindow window)
            return;

        window.Closed -= OnPlayerWindowClosed;
        if (_shutdown?.IsPreparing == true ||
            _app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow != window)
            return;

        var next = desktop.Windows.OfType<PlayerWindow>().FirstOrDefault(other => other != window && other.IsVisible);
        if (next != null)
            desktop.MainWindow = next;
        else
            _ = CompleteLocalShutdownAsync();
    }

    private void FinishPlayerShutdown()
    {
        if (_finishedShutdown)
            return;

        _finishedShutdown = true;
        try
        {
            Dispose();
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Player shutdown: failed to exit the desktop application");
        }
        finally
        {
            try
            {
                if (_serviceProvider is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Player shutdown: failed to dispose the service provider");
            }
        }
    }
}
