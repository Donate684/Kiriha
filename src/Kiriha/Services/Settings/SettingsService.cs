using Kiriha.Services.Data.Settings;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Utils.Async;
using Serilog;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService : IDisposable, ISettingsService
{
    private readonly string _settingsPath;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private string? _lastSavedJson;
    private readonly Lock _stateLock = new();
    private long _uiVersion;
    private long _systemVersion;
    private long _playerVersion;
    private long _torrentsVersion;
    private long _apiVersion;
    private long _customLinksVersion;

    private AppSettings _current = new();
    public AppSettings Current => Volatile.Read(ref _current);

    public SettingsService(string? settingsPath = null)
    {
        var sw = Stopwatch.StartNew();
        _settingsPath = settingsPath ?? Kiriha.Infrastructure.Platform.PathHelper.GetSettingsPath();
        _debouncer = new Debouncer(TimeSpan.FromMilliseconds(500), async (_) => await SaveAsync());
        Load();
        Log.Information("StartupTiming: settings service initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
    }

    public void Update(Action<AppSettings> update, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_stateLock)
        {
            var clone = CloneSettings(_current);
            update(clone);
            Volatile.Write(ref _current, clone);
            MarkAllSectionsChanged();
        }
        
        if (save) Save();
    }

    public void Update(Action<AppSettings> update, SettingsSection changedSections, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_stateLock)
        {
            var clone = CloneSettings(_current);
            update(clone);
            Volatile.Write(ref _current, clone);
            MarkChangedSections(changedSections);
        }
        
        if (save) Save();
    }

    public T Read<T>(Func<AppSettings, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        lock (_stateLock)
        {
            return read(_current);
        }
    }



    private SettingsVersions GetVersions() => new(
        _uiVersion,
        _systemVersion,
        _playerVersion,
        _torrentsVersion,
        _apiVersion,
        _customLinksVersion);

    private void MarkVersionsSaved(SettingsVersions versions)
    {
        lock (_stateLock)
        {
            if (_uiVersion == versions.Ui) _uiVersion = 0;
            if (_systemVersion == versions.System) _systemVersion = 0;
            if (_playerVersion == versions.Player) _playerVersion = 0;
            if (_torrentsVersion == versions.Torrents) _torrentsVersion = 0;
            if (_apiVersion == versions.Api) _apiVersion = 0;
            if (_customLinksVersion == versions.CustomLinks) _customLinksVersion = 0;
        }
    }

    private void MarkChangedSections(SettingsSection sections)
    {
        lock (_stateLock)
        {
            if (sections.HasFlag(SettingsSection.UI)) _uiVersion++;
            if (sections.HasFlag(SettingsSection.System)) _systemVersion++;
            if (sections.HasFlag(SettingsSection.Player)) _playerVersion++;
            if (sections.HasFlag(SettingsSection.Torrents)) _torrentsVersion++;
            if (sections.HasFlag(SettingsSection.Api)) _apiVersion++;
            if (sections.HasFlag(SettingsSection.CustomLinks)) _customLinksVersion++;
        }
    }

    private void MarkAllSectionsChanged()
    {
        lock (_stateLock)
        {
            _uiVersion++;
            _systemVersion++;
            _playerVersion++;
            _torrentsVersion++;
            _apiVersion++;
            _customLinksVersion++;
        }
    }








    private void SetCurrent(AppSettings settings)
    {
        lock (_stateLock)
        {
            Volatile.Write(ref _current, settings);
        }
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, AppSettingsJsonContext.Default.AppSettings);
        return JsonSerializer.Deserialize(bytes, AppSettingsJsonContext.Default.AppSettings)!;
    }
}
