using System;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;
#if WINDOWS
using Windows.Media.Control;
#endif

namespace Kiriha.Infrastructure.Tracking.Integration;

public class SmtcService : IDisposable
{
    private readonly ISettingsService _settingsService;
#if WINDOWS
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
#endif

    public bool DiscoveryMode { get; set; }

    public SmtcService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task StartAsync()
    {
        try
        {
#if WINDOWS
            if (_manager == null && OperatingSystem.IsWindows())
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
        if (_manager == null || !OperatingSystem.IsWindows()) return null;

        try
        {
            var sessions = _manager.GetSessions();
            var session = sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains(processName, StringComparison.OrdinalIgnoreCase))
                          ?? _manager.GetCurrentSession();

            if (session != null)
            {
                var timeline = session.GetTimelineProperties();
                if (timeline != null && timeline.EndTime > TimeSpan.Zero)
                {
                    return (timeline.Position, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime);
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
