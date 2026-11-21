using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Mapping;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class ManualMappingShutdownHandler(
    ManualMappingService? manualMappingService = null,
    IScrobbleProgressStore? scrobbleProgressStore = null,
    IPlaybackResumeStore? playbackResumeStore = null) : IShutdownHandler
{
    public async Task FlushAsync() => await TryFlushAsync();

    public async Task<bool> TryFlushAsync()
    {
        const int timeoutMs = 1500;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        var token = timeout.Token;

        try
        {
            await Task.WhenAll(
                manualMappingService?.FlushAsync(token) ?? Task.CompletedTask,
                scrobbleProgressStore?.FlushAsync(token) ?? Task.CompletedTask,
                playbackResumeStore?.FlushAsync(token) ?? Task.CompletedTask).WaitAsync(token);
            return true;
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            Log.Warning(ex, "Shutdown flush: local state stores exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: local state could not be fully saved");
        }
        return false;
    }
}
