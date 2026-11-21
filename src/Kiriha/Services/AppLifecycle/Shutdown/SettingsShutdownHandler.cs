using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Services.AppLifecycle.Shutdown;

public sealed class SettingsShutdownHandler : IShutdownHandler
{
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(2);
    private readonly ISettingsService _settingsService;

    public SettingsShutdownHandler(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task FlushAsync() => await TryFlushAsync();

    public async Task<bool> TryFlushAsync()
    {
        try
        {
            await _settingsService.SaveAsync().WaitAsync(SaveTimeout);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: ISettingsService.SaveAsync failed");
            return false;
        }
    }
}
