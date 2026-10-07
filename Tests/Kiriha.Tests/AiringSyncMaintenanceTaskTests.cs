using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.Maintenance;
using Moq;

namespace Kiriha.Tests;

public class AiringSyncMaintenanceTaskTests
{
    [Fact]
    public void AiringSyncMaintenanceTask_InitialDelay_IsShortForFastAiringStartup()
    {
        var mock = new Mock<IAiringInfoService>();
        var task = new AiringSyncMaintenanceTask(mock.Object);
        Assert.Equal(TimeSpan.FromSeconds(10), task.InitialDelay);
        Assert.Equal(TimeSpan.FromHours(6), task.Interval);
    }

    [Fact]
    public async Task AiringSyncMaintenanceTask_ExecuteAsync_CallsSyncOngoingEpisodesAsync()
    {
        var mock = new Mock<IAiringInfoService>();
        var task = new AiringSyncMaintenanceTask(mock.Object);
        await task.ExecuteAsync(CancellationToken.None);
        mock.Verify(x => x.SyncOngoingEpisodesAsync(false, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
