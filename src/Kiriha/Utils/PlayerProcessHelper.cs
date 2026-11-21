using System.Diagnostics;
using Kiriha.Infrastructure.Player;

namespace Kiriha.Utils;

public static class PlayerProcessHelper
{
    internal static Action<string?, string?, string?, string?, int?, string?, string?, string?, IReadOnlyDictionary<string, string>?>? LaunchOverrideForTests { get; set; }
    internal static Func<CancellationToken, Task<bool>>? OpeningOverrideForTests { get; set; }

    public static void LaunchPlayer() => LaunchPlayer(videoUrl: null);

    public static void LaunchPlayer(
        string? videoUrl,
        string? titleRu = null,
        string? titleEn = null,
        string? episode = null,
        int? animeId = null,
        string? torrentHash = null,
        string? originalTitle = null,
        string? subtitleUrl = null,
        IReadOnlyDictionary<string, string>? headers = null) =>
        LaunchPlayerAsync(videoUrl, titleRu, titleEn, episode, animeId, torrentHash, originalTitle, subtitleUrl, headers)
            .GetAwaiter().GetResult();

    public static async Task<bool> LaunchPlayerAsync(
        string? videoUrl, string? titleRu = null, string? titleEn = null, string? episode = null,
        int? animeId = null, string? torrentHash = null, string? originalTitle = null,
        string? subtitleUrl = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default,
        string? requestId = null)
    {
        ct.ThrowIfCancellationRequested();
        if (LaunchOverrideForTests is { } testHook)
        {
            testHook(videoUrl, titleRu, titleEn, episode, animeId, torrentHash, originalTitle, subtitleUrl, headers);
            return OpeningOverrideForTests is not { } opening || await opening(ct).ConfigureAwait(false);
        }
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return false;

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var isDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (isDotnet && string.IsNullOrEmpty(assemblyPath)) return false;

        requestId ??= Guid.NewGuid().ToString("N");
        if (!PlayerPlaybackProtocol.IsValidRequestId(requestId))
            return false;

        var startInfo = CreatePlayerStartInfo(processPath, assemblyPath, videoUrl, titleRu, titleEn, episode,
            animeId, torrentHash, originalTitle, subtitleUrl, headers);
        startInfo.ArgumentList.Add(PlayerProcessBridge.PlaybackRequestArg);
        startInfo.ArgumentList.Add(requestId);
        startInfo.ArgumentList.Add(PlayerPlaybackProtocol.AdmissionDeadlineArg);
        startInfo.ArgumentList.Add(PlayerPlaybackProtocol.CreateAdmissionDeadline());
        var command = startInfo.ArgumentList.Skip(isDotnet ? 1 : 0).ToArray();
        var bootstrap = CreatePlayerStartInfo(processPath, assemblyPath, videoUrl: null);
        bootstrap.ArgumentList.Add(PlayerPlaybackProtocol.BootstrapArg);
        var client = new PlayerLaunchClient(PlayerProcessBridge.IsPlayerInstanceRunning,
            () => TryStartPlayer(bootstrap), PlayerProcessBridge.ExchangePlaybackAsync);
        return await client.OpenAsync(requestId, command, ct).ConfigureAwait(false) == PlayerLaunchStatus.Ready;
    }

    private static bool TryStartPlayer(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return process != null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to launch player process");
            return false;
        }
    }

    public static ProcessStartInfo CreatePlayerStartInfo(
        string processPath,
        string? assemblyPath,
        string? videoUrl,
        string? titleRu = null,
        string? titleEn = null,
        string? episode = null,
        int? animeId = null,
        string? torrentHash = null,
        string? originalTitle = null,
        string? subtitleUrl = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var canonicalOriginal = !string.IsNullOrWhiteSpace(originalTitle)
            ? originalTitle
            : (titleEn ?? titleRu ?? string.Empty);

        var isDotnet = System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        if (isDotnet && !string.IsNullOrEmpty(assemblyPath))
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add("--player");

        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            startInfo.ArgumentList.Add(videoUrl);
        }
        if (!string.IsNullOrWhiteSpace(subtitleUrl))
        {
            startInfo.ArgumentList.Add("--sub-file");
            startInfo.ArgumentList.Add(subtitleUrl);
        }
        if (!string.IsNullOrWhiteSpace(canonicalOriginal))
        {
            startInfo.ArgumentList.Add("--original-title");
            startInfo.ArgumentList.Add(canonicalOriginal);
        }
        if (!string.IsNullOrWhiteSpace(titleRu))
        {
            startInfo.ArgumentList.Add("--title-ru");
            startInfo.ArgumentList.Add(titleRu);
        }
        if (!string.IsNullOrWhiteSpace(titleEn))
        {
            startInfo.ArgumentList.Add("--title-en");
            startInfo.ArgumentList.Add(titleEn);
        }
        if (!string.IsNullOrWhiteSpace(episode))
        {
            startInfo.ArgumentList.Add("--episode");
            startInfo.ArgumentList.Add(episode);
        }
        if (animeId.HasValue)
        {
            startInfo.ArgumentList.Add("--anime-id");
            startInfo.ArgumentList.Add(animeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(torrentHash))
        {
            startInfo.ArgumentList.Add("--torrent-hash");
            startInfo.ArgumentList.Add(torrentHash);
        }
        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                startInfo.ArgumentList.Add("--http-header");
                startInfo.ArgumentList.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        if (OperatingSystem.IsWindows())
        {
            startInfo.KillOnParentExit = true;
        }

        return startInfo;
    }

    public static bool IsTorrentStream(string? url, string[]? args = null)
    {
        if (args != null && args.Any(a => a.Equals("--torrent-hash", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (System.Text.RegularExpressions.Regex.IsMatch(url, Kiriha.Core.Domain.Constants.AppConstants.Torrents.TorrentHashLinkRegex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(500)))
            return true;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            (uri.IsLoopback || string.Equals(uri.Host, Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.DefaultHost, StringComparison.OrdinalIgnoreCase)) &&
            (uri.AbsolutePath.StartsWith("/stream", StringComparison.OrdinalIgnoreCase) ||
             uri.AbsolutePath.StartsWith("/play", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
