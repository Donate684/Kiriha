using System;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Services.Data.Settings;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class SettingsShutdownHandler : IShutdownHandler
{
    private readonly ISettingsService _settingsService;

    public SettingsShutdownHandler(ISettingsService settingsService)
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
            Log.Error(ex, "Shutdown flush: ISettingsService.SaveAsync failed");
        }
    }
}
