using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.Maintenance;

public class AiringSyncMaintenanceTask : IMaintenanceTask
{
    private readonly IAiringInfoService _airingService;

    public AiringSyncMaintenanceTask(IAiringInfoService airingService)
    {
        _airingService = airingService;
    }

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(10);
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public Task ExecuteAsync(CancellationToken ct) =>
        _airingService.SyncOngoingEpisodesAsync(false, null, ct);
}
