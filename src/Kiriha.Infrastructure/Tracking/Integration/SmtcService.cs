using Serilog;
#if WINDOWS
using Windows.Media.Control;
#endif

namespace Kiriha.Infrastructure.Tracking.Integration;

public class SmtcService : IDisposable
{
#if WINDOWS
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
#endif

    public bool DiscoveryMode { get; set; }

    public SmtcService()
    {
    }

    public async Task StartAsync()
    {
        try
        {
#if WINDOWS
            if (_manager is null && OperatingSystem.IsWindows())
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                Log.Information("SMTC Session Manager initialized.");
            }
#endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize SMTC Session Manager");
        }
    }

    public (TimeSpan Position, TimeSpan Duration, DateTimeOffset LastUpdatedTime)? GetTimeline(string processName)
    {
#if WINDOWS
        if (string.IsNullOrWhiteSpace(processName) || _manager is null || !OperatingSystem.IsWindows()) return null;

        try
        {
            var sessions = _manager.GetSessions();
            var session = sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains(processName, StringComparison.OrdinalIgnoreCase));

            if (session != null)
            {
                var timeline = session.GetTimelineProperties();
                if (timeline != null && timeline.EndTime > TimeSpan.Zero)
                {
                    var position = timeline.Position;
                    var duration = timeline.EndTime - timeline.StartTime;

                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo != null &&
                        playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                        timeline.LastUpdatedTime.Year >= 2020)
                    {
                        var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                        if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(12))
                        {
                            double rate = playbackInfo.PlaybackRate ?? 1.0;
                            if (!double.IsFinite(rate) || rate <= 0) rate = 1.0;

                            var extrapolated = position + TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
                            if (extrapolated > timeline.EndTime) extrapolated = timeline.EndTime;
                            else if (extrapolated < timeline.StartTime) extrapolated = timeline.StartTime;
                            position = extrapolated;
                        }
                    }

                    return (position, duration, timeline.LastUpdatedTime);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error reading SMTC timeline for {ProcessName}", processName);
        }
#endif

        return null;
    }

    public void RequestRefresh()
    {
    }

    public void Dispose()
    {
    }
}
