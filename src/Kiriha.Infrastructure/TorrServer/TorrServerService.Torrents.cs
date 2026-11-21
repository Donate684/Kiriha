using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.TorrServer;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

public sealed partial class TorrServerService
{
    public async Task<TorrServerTorrentStatus?> AddTorrentAsync(string link, string title, CancellationToken ct = default)
    {
        var endpoint = ServerUrl;
        if (!await EnsureRunningAsync(ct).ConfigureAwait(false) || ServerUrl != endpoint)
            return null;

        await using var gate = await _usage.LockEndpointAsync(endpoint, ct).ConfigureAwait(false);
        var status = await AddTorrentAtAsync(endpoint, link, title, ct).ConfigureAwait(false);
        return status == null ? null : await GetTorrentAsync(status.Hash, ct).ConfigureAwait(false) ?? status;
    }

    public async Task<TorrServerPreparation?> PrepareTorrentAsync(string link, string title, CancellationToken ct = default)
    {
        var endpoint = ServerUrl;
        if (!await EnsureRunningAsync(ct).ConfigureAwait(false) || ServerUrl != endpoint)
            return null;

        await using var gate = await _usage.LockEndpointAsync(endpoint, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // Once sent, read the hash even if the launch is cancelled so a late addition can be released.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        TorrServerTorrentStatus? status;
        try
        {
            status = await AddTorrentAtAsync(endpoint, link, title, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            Logger.Warning(ex, "TorrServerService: Adding torrent {Title} timed out", title);
            return null;
        }
        if (status == null)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }

        var lease = _usage.AcquireLocked(endpoint, status.Hash);
        if (ct.IsCancellationRequested)
        {
            lease.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return new TorrServerPreparation(endpoint, status, lease);
    }

    private async Task<TorrServerTorrentStatus?> AddTorrentAtAsync(string endpoint, string link, string title, CancellationToken ct)
    {
        try
        {
            var finalLink = link;
            if (finalLink.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) &&
                !finalLink.Contains("&tr=", StringComparison.OrdinalIgnoreCase))
            {
                finalLink = $"{finalLink}{AppConstants.Torrents.NyaaDefaultTrackersQuery}";
            }

            var payload = new
            {
                action = "add",
                link = finalLink,
                title,
                save_to_db = false
            };

            using var response = await _httpClient.PostAsJsonAsync($"{endpoint}/torrents", payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("TorrServerService: AddTorrent failed with HTTP {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
            if (json == null)
                return null;

            var hash = ReadString(json["hash"], string.Empty);
            return string.IsNullOrWhiteSpace(hash) ? null
                : new TorrServerTorrentStatus { Hash = hash, Title = title };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.Error(ex, "TorrServerService: Error adding torrent '{Title}'", title);
            return null;
        }
    }

    public async Task<TorrServerTorrentStatus?> GetTorrentAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;

        try
        {
            var payload = new { action = "get", hash };
            using var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/torrents", payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
            if (json == null)
                return null;

            var files = new List<TorrServerFileItem>();
            if (json["file_stats"] is JsonArray fileArray)
            {
                foreach (var node in fileArray)
                {
                    if (node is JsonObject fileObj)
                    {
                        var id = ReadInt32(fileObj["id"], 0);
                        var path = ReadString(fileObj["path"], string.Empty);
                        var length = ReadInt64(fileObj["length"], 0L);
                        files.Add(new TorrServerFileItem
                        {
                            Id = id,
                            Path = path,
                            Length = length
                        });
                    }
                }
            }

            return new TorrServerTorrentStatus
            {
                Hash = ReadString(json["hash"], hash),
                Title = ReadString(json["title"], string.Empty),
                StatString = ReadString(json["stat_string"], string.Empty),
                Stat = ReadInt32(json["stat"], 0),
                TorrentSize = ReadInt64(json["torrent_size"], 0L),
                DownloadSpeed = ReadDouble(json["download_speed"], 0.0),
                UploadSpeed = ReadDouble(json["upload_speed"], 0.0),
                TotalPeers = ReadInt32(json["total_peers"], 0),
                ActivePeers = ReadInt32(json["active_peers"], 0),
                ConnectedSeeders = ReadInt32(json["connected_seeders"], 0),
                PreloadSize = ReadInt64(json["preload_size"], 0L),
                PreloadedBytes = ReadInt64(json["preloaded_bytes"], 0L),
                Files = files
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.Warning(ex, "TorrServerService: Error querying torrent {Hash}", hash);
            return null;
        }
    }

    public async Task<IReadOnlyList<TorrServerFileItem>> WaitForFilesAsync(string hash, TimeSpan timeout, CancellationToken ct = default)
    {
        const int filesPollIntervalMs = 300;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var status = await GetTorrentAsync(hash, deadline.Token).ConfigureAwait(false);
                if (status != null && status.Files.Count > 0)
                    return status.Files;
                await Task.Delay(filesPollIntervalMs, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }

    public async Task DropTorrentAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return;

        try
        {
            await _usage.RemoveIfUnusedAsync(ServerUrl, hash, requireMarker: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.Warning(ex, "TorrServerService: Failed to schedule removal of torrent {Hash}", hash);
        }
    }

    public async Task<IDisposable> AcquirePlaybackAsync(string streamUrl, string hash, CancellationToken ct = default)
    {
        if (TorrServerStreamSource.Parse(streamUrl) is not { } source ||
            !string.Equals(source.Hash, hash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The stream URL does not identify the requested torrent.", nameof(streamUrl));

        await using var gate = await _usage.LockEndpointAsync(source.Endpoint, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return _usage.AcquireLocked(source.Endpoint, source.Hash);
    }

    public Task FlushTorrentReleasesAsync(CancellationToken ct = default) => _usage.FlushAsync(ct);

    private async Task<bool> RemoveTorrentAsync(string endpoint, string hash, CancellationToken ct)
    {
        try
        {
            var payload = new { action = "rem", hash };
            using var response = await _httpClient.PostAsJsonAsync($"{endpoint}/torrents", payload, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return true;
            response.EnsureSuccessStatusCode();
            Logger.Information("TorrServerService: Dropped torrent {Hash} from RAM cache", hash);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Failed to drop torrent {Hash}", hash);
            return false;
        }
    }

    private static int ReadInt32(JsonNode? node, int fallback = 0)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)d;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt32(out var elInt)) return elInt;
                if (el.TryGetInt64(out var elLong) && elLong is >= int.MinValue and <= int.MaxValue) return (int)elLong;
                if (el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble)) return (int)elDouble;
            }
            else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static long ReadInt64(JsonNode? node, long fallback = 0L)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return (long)d;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt64(out var elLong)) return elLong;
                if (el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble)) return (long)elDouble;
            }
            else if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static double ReadDouble(JsonNode? node, double fallback = 0.0)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return d;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble))
                return elDouble;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
                return parsed;
        }
        return fallback;
    }

    private static bool ReadBoolean(JsonNode? node, bool fallback = false)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<bool>(out var b)) return b;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    private static string ReadString(JsonNode? node, string fallback = "")
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<string>(out var s) && s != null) return s;
        if (val.TryGetValue<JsonElement>(out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? fallback;
        return fallback;
    }
}
