using System;
using System.Threading.Tasks;
using Kiriha.Services.Data.Settings;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class SettingsShutdownHandler : IShutdownHandler
{
    private readonly Kiriha.Core.Services.ISettingsService _settingsService;

    public SettingsShutdownHandler(Kiriha.Core.Services.ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task FlushAsync()
    {
        try
        {
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: Kiriha.Core.Services.ISettingsService.SaveAsync failed");
        }
    }
}
