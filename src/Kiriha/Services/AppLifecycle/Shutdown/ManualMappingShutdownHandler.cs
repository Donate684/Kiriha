using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Mapping;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class ManualMappingShutdownHandler : IShutdownHandler
{
    private readonly ManualMappingService _manualMappingService;
    private readonly IScrobbleProgressStore? _scrobbleProgressStore;
    private readonly IPlaybackResumeStore? _playbackResumeStore;

    public ManualMappingShutdownHandler(
        ManualMappingService manualMappingService,
        IScrobbleProgressStore? scrobbleProgressStore = null,
        IPlaybackResumeStore? playbackResumeStore = null)
    {
        _manualMappingService = manualMappingService;
        _scrobbleProgressStore = scrobbleProgressStore;
        _playbackResumeStore = playbackResumeStore;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 1500;
        try
        {
            var tasks = new List<Task> { _manualMappingService.FlushAsync() };
            if (_scrobbleProgressStore != null)
                tasks.Add(_scrobbleProgressStore.FlushAsync());
            if (_playbackResumeStore != null)
                tasks.Add(_playbackResumeStore.FlushAsync());

            var combined = Task.WhenAll(tasks);
            if (!await ShutdownTimeout.WaitForAsync(combined, timeoutMs))
                Log.Warning("Shutdown flush: SQLite state stores exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: SQLite state stores flush failed");
        }
    }
}
