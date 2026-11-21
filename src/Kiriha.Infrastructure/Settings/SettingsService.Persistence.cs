using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Infrastructure.Settings;

public partial class SettingsService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public void SaveImmediate()
    {
        bool lockTaken = false;
        try
        {
            lockTaken = _saveLock.Wait(TimeSpan.FromSeconds(2));
            if (lockTaken)
            {
                InternalSaveSync();
            }
            else
            {
                Log.Warning("SettingsService: SaveImmediate timed out waiting for save lock");
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public async Task SaveAsync()
    {
        bool lockTaken = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (CanSkipSave())
                return;

            await Task.Run(InternalSaveSync).ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private void InternalSaveSync()
    {
        if (!CanSkipSave())
            WithFileLock(SaveFiles);
    }

    private void SaveFiles()
    {
        EnsureDirectory();

        AppSettings snapshot;
        SettingsVersions versions;
        lock (_stateLock)
        {
            snapshot = CloneSettings(_current);
            versions = GetVersions();
        }

        var json = AppSettingsJsonContext.Default;
        if (versions.Ui != 0 || versions.System != 0 || versions.CustomLinks != 0 || !File.Exists(_appSettingsPath))
            SaveDocument(_appSettingsPath, GetAppConfig(snapshot), json.AppConfigFile, ref _savedApp);
        if (versions.Player != 0 || !File.Exists(_playerSettingsPath))
            SaveDocument(_playerSettingsPath, snapshot.Player, json.PlayerConfig, ref _savedPlayer);
        if (versions.Torrents != 0 || !File.Exists(_torrentsSettingsPath))
            SaveDocument(_torrentsSettingsPath, snapshot.Torrents, json.TorrentConfig, ref _savedTorrents, SerializeTorrents);
        if (versions.Api != 0 || !File.Exists(_authSettingsPath))
            SaveDocument(_authSettingsPath, snapshot.Api, json.ApiConfig, ref _savedAuth, SerializeAuth);
        if (versions.Window != 0 || !File.Exists(_windowSettingsPath))
            SaveDocument(_windowSettingsPath, snapshot.UI.Window, json.WindowPlacement, ref _savedWindow);

        MarkVersionsSaved(versions);
    }

    private void SaveDocument<T>(string path, T snapshot, JsonTypeInfo<T> typeInfo, ref JsonObject? baseline,
        Func<T, string>? serializeProtected = null) where T : class
    {
        var current = ToJsonObject(snapshot, typeInfo);
        if (JsonNode.DeepEquals(baseline, current) && File.Exists(path))
            return;

        var latest = ReadDocument(path, typeInfo, out bool hasValidPrimary);
        if (latest != null && baseline == null)
        {
            // A document already initialized by another process wins over our defaults.
            baseline = current;
            return;
        }

        var encoded = serializeProtected == null
            ? current
            : JsonNode.Parse(serializeProtected(snapshot))?.AsObject() ?? throw new JsonException("Could not serialize protected settings.");
        if (latest != null && baseline != null)
        {
            if (ApplyLocalChanges(baseline, current, encoded, latest) || !hasValidPrimary)
                AtomicWrite(path, latest.ToJsonString(IndentedJson), hasValidPrimary);
        }
        else
        {
            AtomicWrite(path, encoded.ToJsonString(IndentedJson), hasValidPrimary);
        }

        // Advance each baseline only after its own write. Retrying another file must
        // not overwrite a value that a different process changed after this write.
        baseline = current;
    }

    private static AppConfigFile GetAppConfig(AppSettings settings) => new()
    {
        UI = settings.UI,
        System = settings.System,
        CustomLinks = settings.CustomLinks
    };

    private static JsonObject ToJsonObject<T>(T settings, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToNode(settings, typeInfo)?.AsObject()
            ?? throw new JsonException("Could not serialize settings.");

    private string SerializeAuth(AppSettings.ApiConfig api)
    {
        var clone = api.Clone();
        EncryptApiConfig(clone);
        return JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.ApiConfig);
    }

    private string SerializeTorrents(AppSettings.TorrentConfig torrents)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(torrents, AppSettingsJsonContext.Default.TorrentConfig);
        var clone = JsonSerializer.Deserialize(bytes, AppSettingsJsonContext.Default.TorrentConfig)!;
        EncryptOnlineStreamingConfig(clone);
        return JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.TorrentConfig);
    }

    private bool CanSkipSave()
    {
        lock (_stateLock)
        {
            return _uiVersion == 0
                && _systemVersion == 0
                && _playerVersion == 0
                && _torrentsVersion == 0
                && _apiVersion == 0
                && _customLinksVersion == 0
                && _windowVersion == 0
                && File.Exists(_appSettingsPath)
                && File.Exists(_playerSettingsPath)
                && File.Exists(_torrentsSettingsPath)
                && File.Exists(_authSettingsPath)
                && File.Exists(_windowSettingsPath);
        }
    }

    private readonly record struct SettingsVersions(
        long Ui,
        long System,
        long Player,
        long Torrents,
        long Api,
        long CustomLinks,
        long Window);

    private void EnsureDirectory()
    {
        if (!string.IsNullOrEmpty(_settingsDir) && !Directory.Exists(_settingsDir))
            Directory.CreateDirectory(_settingsDir);
    }
}
