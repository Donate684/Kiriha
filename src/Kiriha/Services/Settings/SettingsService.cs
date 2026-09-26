using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Services.Data.Settings;

public partial class SettingsService : IDisposable, ISettingsService
{
    private string _settingsDir = null!;
    private string _legacySettingsPath = null!;
    private string _appSettingsPath = null!;
    private string _playerSettingsPath = null!;
    private string _torrentsSettingsPath = null!;
    private string _authSettingsPath = null!;
    private string _windowSettingsPath = null!;

    public string SettingsDirectory => _settingsDir;
    public string AppSettingsPath => _appSettingsPath;
    public string PlayerSettingsPath => _playerSettingsPath;
    public string TorrentsSettingsPath => _torrentsSettingsPath;
    public string AuthSettingsPath => _authSettingsPath;
    public string WindowSettingsPath => _windowSettingsPath;
    public string LegacySettingsPath => _legacySettingsPath;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Lock _stateLock = new();

    private string? _lastSavedAppJson;
    private string? _lastSavedPlayerJson;
    private string? _lastSavedTorrentsJson;
    private string? _lastSavedAuthJson;
    private string? _lastSavedWindowJson;

    private long _uiVersion;
    private long _systemVersion;
    private long _playerVersion;
    private long _torrentsVersion;
    private long _apiVersion;
    private long _customLinksVersion;
    private long _windowVersion;

    private AppSettings _current = new();
    public AppSettings Current => Volatile.Read(ref _current);

    public SettingsService(string? settingsPathOrDir = null)
    {
        var sw = Stopwatch.StartNew();
        ResolvePaths(settingsPathOrDir);
        _debouncer = new Debouncer(TimeSpan.FromMilliseconds(500), async (_) => await SaveAsync());
        Load();
        Log.Information("StartupTiming: settings service initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
    }

    private void ResolvePaths(string? settingsPathOrDir)
    {
        if (string.IsNullOrEmpty(settingsPathOrDir))
        {
            _settingsDir = PathHelper.GetSettingsDirPath();
            _legacySettingsPath = PathHelper.GetLegacySettingsPath();
        }
        else
        {
            if (Directory.Exists(settingsPathOrDir) || !Path.HasExtension(settingsPathOrDir))
            {
                _settingsDir = settingsPathOrDir;
                _legacySettingsPath = Path.Combine(Directory.GetParent(settingsPathOrDir)?.FullName ?? settingsPathOrDir, AppConstants.System.FileNames.LegacySettings);
            }
            else
            {
                var dir = Path.GetDirectoryName(settingsPathOrDir)!;
                if (string.IsNullOrEmpty(dir)) dir = ".";

                _legacySettingsPath = settingsPathOrDir;
                _settingsDir = dir;
            }
        }

        _appSettingsPath = Path.Combine(_settingsDir, AppConstants.System.FileNames.SettingsApp);
        _playerSettingsPath = Path.Combine(_settingsDir, AppConstants.System.FileNames.SettingsPlayer);
        _torrentsSettingsPath = Path.Combine(_settingsDir, AppConstants.System.FileNames.SettingsTorrents);
        _authSettingsPath = Path.Combine(_settingsDir, AppConstants.System.FileNames.SettingsAuth);
        _windowSettingsPath = Path.Combine(_settingsDir, AppConstants.System.FileNames.SettingsWindow);
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
        _customLinksVersion,
        _windowVersion);

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
            if (_windowVersion == versions.Window) _windowVersion = 0;
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
            if (sections.HasFlag(SettingsSection.Window)) _windowVersion++;
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
            _windowVersion++;
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
        var clone = JsonSerializer.Deserialize(bytes, AppSettingsJsonContext.Default.AppSettings)!;
        if (settings.UI?.HiddenSeasonalIds != null)
            clone.UI.HiddenSeasonalIds = new List<int>(settings.UI.HiddenSeasonalIds);
        if (settings.UI?.Window != null)
            clone.UI.Window = new AppSettings.WindowPlacement
            {
                Width = settings.UI.Window.Width,
                Height = settings.UI.Window.Height,
                X = settings.UI.Window.X,
                Y = settings.UI.Window.Y,
                Maximized = settings.UI.Window.Maximized
            };
        if (settings.Torrents?.HiddenAnimeIds != null)
            clone.Torrents.HiddenAnimeIds = new List<int>(settings.Torrents.HiddenAnimeIds);
        if (settings.Torrents?.PerTitleFilters != null)
            clone.Torrents.PerTitleFilters = new Dictionary<int, AppSettings.TorrentFilterSet>(settings.Torrents.PerTitleFilters);
        return clone;
    }
}
