using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private string? _previousMediaPath;
    private string? _nextMediaPath;

    private CancellationTokenSource? _updateNavigationCts;

    private void UpdateNavigationAvailability()
    {
        _previousMediaPath = null;
        _nextMediaPath = null;
        CanOpenPreviousMedia = false;
        CanOpenNextMedia = false;

        var videoUrl = VideoUrl;
        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        _updateNavigationCts?.Cancel();
        _updateNavigationCts?.Dispose();
        _updateNavigationCts = new CancellationTokenSource();

        _ = Task.Run(() => UpdateNavigationAvailabilityAsync(videoUrl, _updateNavigationCts.Token));
    }

    private void UpdateNavigationAvailabilityAsync(string videoUrl, CancellationToken token)
    {
        if (!File.Exists(videoUrl))
            return;

        var directory = Path.GetDirectoryName(videoUrl);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            var files = Directory.EnumerateFiles(directory)
                .Where(IsSupportedMediaPath)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (token.IsCancellationRequested) return;

            var currentIndex = files.FindIndex(x => string.Equals(x, videoUrl, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0)
            {
                string? previousMediaPath = null;
                string? nextMediaPath = null;
                bool canOpenPreviousMedia = false;
                bool canOpenNextMedia = false;

                if (currentIndex > 0)
                {
                    previousMediaPath = files[currentIndex - 1];
                    canOpenPreviousMedia = true;
                }

                if (currentIndex < files.Count - 1)
                {
                    nextMediaPath = files[currentIndex + 1];
                    canOpenNextMedia = true;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;

                    _previousMediaPath = previousMediaPath;
                    _nextMediaPath = nextMediaPath;
                    CanOpenPreviousMedia = canOpenPreviousMedia;
                    CanOpenNextMedia = canOpenNextMedia;
                });
            }
        }
        catch
        {
            // Ignore directory access errors
        }
    }

    private static bool IsSupportedMediaPath(string path)
    {
        return MediaExtensions.Contains(Path.GetExtension(path));
    }
}
