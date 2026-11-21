using Kiriha.Views.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

/// <summary>Tracks open player windows and cleanup still running after a window has closed.</summary>
internal sealed class PlayerWindowShutdown
{
    private readonly HashSet<PlayerWindow> _open = [];
    private readonly List<Task<bool>> _closing = [];

    internal bool IsClosing { get; private set; }

    // The registry is accessed on the UI thread.
    internal void Track(PlayerWindow window)
    {
        _open.Add(window);
        window.Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not PlayerWindow window)
            return;

        window.Closed -= OnClosed;
        _open.Remove(window);
        _closing.RemoveAll(task => task.IsCompletedSuccessfully && task.Result);
        _closing.Add(window.ShutdownCompletion);
    }

    internal IReadOnlyList<Task<bool>> CloseAll()
    {
        IsClosing = true;
        foreach (var window in _open.ToArray())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Player shutdown: failed to close a player window");
                _closing.Add(Task.FromResult(false));
            }
        }

        if (_open.Count != 0)
            _closing.Add(Task.FromResult(false));

        return _closing.ToArray();
    }
}
