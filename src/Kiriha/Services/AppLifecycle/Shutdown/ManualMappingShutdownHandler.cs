using Kiriha.Services.Data.Mapping;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class ManualMappingShutdownHandler : IShutdownHandler
{
    private readonly ManualMappingService _manualMappingService;

    public ManualMappingShutdownHandler(ManualMappingService manualMappingService)
    {
        _manualMappingService = manualMappingService;
    }

    public async Task FlushAsync()
    {
        const int timeoutMs = 1500;
        try
        {
            var operation = _manualMappingService.FlushAsync();
            if (!await ShutdownTimeout.WaitForAsync(operation, timeoutMs))
                Log.Warning("Shutdown flush: ManualMappingService.FlushAsync exceeded {Ms}ms timeout", timeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: ManualMappingService.FlushAsync failed");
        }
    }
}
