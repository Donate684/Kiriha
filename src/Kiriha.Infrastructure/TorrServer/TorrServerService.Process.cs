using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kiriha.Core.Domain.Constants;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

public sealed partial class TorrServerService
{
    private enum ServerProbe { Unreachable, OtherService, Ready }

    internal async Task<bool> PingServerAsync(CancellationToken ct) =>
        await ProbeServerAsync(ServerUrl, ct).ConfigureAwait(false) == ServerProbe.Ready;

    private async Task<ServerProbe> ProbeServerAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(EchoPingTimeout);
            using var response = await _httpClient.GetAsync($"{url}/echo", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ServerProbe.OtherService;

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return body.Contains("MatriX", StringComparison.OrdinalIgnoreCase) ? ServerProbe.Ready : ServerProbe.OtherService;
        }
        catch (Exception ex) when (ex is HttpRequestException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return ServerProbe.Unreachable;
        }
    }

    private async Task ApplyMemorySettingsAsync(string url, CancellationToken ct, bool isExternalInstance)
    {
        try
        {
            if (isExternalInstance && !await IsServerIdleAsync(url, ct).ConfigureAwait(false))
            {
                Logger.Debug("TorrServerService: External server is busy or its torrent list is unavailable. Skipping settings reset.");
                return;
            }

            var cacheMb = Math.Clamp(_settingsService.Current.Torrents.Streaming.RamCacheMb, 50, 2048);
            long cacheBytes = (long)cacheMb * 1024L * 1024L;

            JsonObject? setsObj = null;
            try
            {
                using var getResp = await _httpClient.PostAsJsonAsync($"{url}/settings", new { action = "get" }, ct).ConfigureAwait(false);
                if (getResp.IsSuccessStatusCode)
                {
                    setsObj = await getResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logger.Debug(ex, "TorrServerService: Could not query current settings before applying memory limits");
            }

            if (setsObj != null)
            {
                var currentCache = ReadInt64(setsObj["CacheSize"], 0L);
                var currentUseDisk = ReadBoolean(setsObj["UseDisk"], true);
                var currentRemoveOnDrop = ReadBoolean(setsObj["RemoveCacheOnDrop"], false);
                var currentTrackers = ReadString(setsObj["DefaultTrackers"], string.Empty);

                bool hasTrackers = !string.IsNullOrWhiteSpace(currentTrackers) && currentTrackers.Contains("nyaa.tracker.wf", StringComparison.OrdinalIgnoreCase);

                // If attaching to an already running RAM-configured server, or if settings already match, DO NOT call action: "set".
                // Calling "set" causes TorrServer to drop all torrents, disconnect all peers, and restart its BitTorrent client.
                if ((isExternalInstance && !currentUseDisk) ||
                    (currentCache == cacheBytes && !currentUseDisk && currentRemoveOnDrop && hasTrackers))
                {
                    Logger.Debug("TorrServerService: Settings already match RAM streaming requirements. Preserving active client session.");
                    return;
                }

                setsObj["CacheSize"] = cacheBytes;
                setsObj["ReaderReadAHead"] = 95;
                setsObj["PreloadCache"] = 50;
                setsObj["UseDisk"] = false;
                setsObj["RemoveCacheOnDrop"] = true;
                setsObj["RetrackersMode"] = 1;
                setsObj["ResponsiveMode"] = true;

                if (string.IsNullOrWhiteSpace(currentTrackers))
                {
                    setsObj["DefaultTrackers"] = AppConstants.Torrents.DefaultPublicTrackers;
                }
                else if (!currentTrackers.Contains("nyaa.tracker.wf", StringComparison.OrdinalIgnoreCase))
                {
                    setsObj["DefaultTrackers"] = $"{AppConstants.Torrents.DefaultPublicTrackers}\n{currentTrackers}";
                }
            }
            else if (isExternalInstance)
            {
                Logger.Debug("TorrServerService: Skipping blind settings override on external TorrServer instance.");
                return;
            }
            else
            {
                setsObj = new JsonObject
                {
                    ["CacheSize"] = cacheBytes,
                    ["ReaderReadAHead"] = 95,
                    ["PreloadCache"] = 50,
                    ["UseDisk"] = false,
                    ["RemoveCacheOnDrop"] = true,
                    ["RetrackersMode"] = 1,
                    ["ResponsiveMode"] = true,
                    ["DefaultTrackers"] = AppConstants.Torrents.DefaultPublicTrackers
                };
            }

            var payload = new
            {
                action = "set",
                sets = setsObj
            };

            using var response = await _httpClient.PostAsJsonAsync($"{url}/settings", payload, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Logger.Information("TorrServerService: RAM streaming cache configured to {Mb} MB (0 disk bytes, public trackers restored)", cacheMb);
                // Allow TorrServer BitTorrent client a moment to finish its internal reload ("end set settings")
                await Task.Delay(800, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.Warning(ex, "TorrServerService: Could not update TorrServer memory cache settings");
        }
    }

    private async Task<bool> IsServerIdleAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"{url}/torrents", new { action = "list" }, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var array = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: ct).ConfigureAwait(false);
            return array is { Count: 0 };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.Debug(ex, "TorrServerService: Cannot confirm external server is idle");
            return false;
        }
    }

    private static Process? StartLocalProcess(int port)
    {
        var exePath = LocateExecutable();
        if (exePath == null)
        {
            Logger.Error("TorrServerService: torrserver.exe was not found. Please run scripts/download-torrserver.ps1");
            return null;
        }

        var dataDir = Platform.PathHelper.GetTorrServerDataPath();
        Directory.CreateDirectory(dataDir);
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = dataDir
        };
        foreach (var argument in new[] { "-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-d", dataDir, "-i", AppConstants.Api.TorrServer.DefaultHost })
            startInfo.ArgumentList.Add(argument);

        if (OperatingSystem.IsWindows())
            startInfo.KillOnParentExit = true;

        Logger.Information("TorrServerService: Launching local TorrServer on port {Port} from {Path}", port, exePath);
        try
        {
            return Process.Start(startInfo);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && startInfo.KillOnParentExit)
        {
            Logger.Warning(ex, "TorrServerService: Process.Start with KillOnParentExit failed; retrying without JobObject binding");
            startInfo.KillOnParentExit = false;
            return Process.Start(startInfo);
        }
    }

    internal static string? LocateExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "torrserver", "torrserver.exe"),
            Path.Combine(baseDir, "torrserver.exe"),
            Path.Combine(Platform.PathHelper.GetTorrServerDataPath(), "torrserver.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "subprojects", "torrserver", "torrserver.exe")),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kiriha", "torrserver", "torrserver.exe")
        };

        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && current != null; i++)
        {
            candidates.Add(Path.Combine(current.FullName, "subprojects", "torrserver", "torrserver.exe"));
            current = current.Parent;
        }

        string? fallbackAny = null;
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            fallbackAny ??= candidate;
            if (IsCompatibleMatriXCandidate(candidate))
                return candidate;
        }

        return fallbackAny;
    }

    private static bool IsCompatibleMatriXCandidate(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir))
                return true;

            var versionFile = Path.Combine(dir, "version.txt");
            if (!File.Exists(versionFile))
                return true;

            var version = File.ReadAllText(versionFile).Trim();
            if (string.IsNullOrEmpty(version) || version.StartsWith("MatriX.", StringComparison.OrdinalIgnoreCase))
                return true;

            Logger.Error("TorrServerService: Skipping incompatible TorrServer binary at {Path} (version.txt='{Version}'; MatriX.* required)", exePath, version);
            return false;
        }
        catch
        {
            return true;
        }
    }
}
