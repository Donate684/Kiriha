using System;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService : IDisposable
{
    private readonly Debouncer _debouncer;

    public void Save() => _debouncer.Invoke();

    public void Dispose()
    {
        try
        {
            _debouncer.CancelPending();
            SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: final save failed during dispose");
        }

        _debouncer.Dispose();
        _saveLock.Dispose();
    }
}
