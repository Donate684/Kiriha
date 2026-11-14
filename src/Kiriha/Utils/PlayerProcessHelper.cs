using System.Diagnostics;
using System.Text;
using Kiriha.Infrastructure.Player;

namespace Kiriha.Utils;

public static class PlayerProcessHelper
{
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
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var canonicalOriginal = !string.IsNullOrWhiteSpace(originalTitle)
            ? originalTitle
            : (titleEn ?? titleRu ?? string.Empty);

        // 1. If resident player instance is running, forward playback and metadata through pipe
        var forwardArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            forwardArgs.Add("--player");
            forwardArgs.Add(videoUrl);
            if (!string.IsNullOrWhiteSpace(subtitleUrl))
            {
                forwardArgs.Add("--sub-file");
                forwardArgs.Add(subtitleUrl);
            }
            if (!string.IsNullOrWhiteSpace(canonicalOriginal))
            {
                forwardArgs.Add("--original-title");
                forwardArgs.Add(canonicalOriginal);
            }
            if (!string.IsNullOrWhiteSpace(titleRu))
            {
                forwardArgs.Add("--title-ru");
                forwardArgs.Add(titleRu);
            }
            if (!string.IsNullOrWhiteSpace(titleEn))
            {
                forwardArgs.Add("--title-en");
                forwardArgs.Add(titleEn);
            }
            if (!string.IsNullOrWhiteSpace(episode))
            {
                forwardArgs.Add("--episode");
                forwardArgs.Add(episode);
            }
            if (animeId.HasValue)
            {
                forwardArgs.Add("--anime-id");
                forwardArgs.Add(animeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(torrentHash))
            {
                forwardArgs.Add("--torrent-hash");
                forwardArgs.Add(torrentHash);
            }
            if (headers != null)
            {
                foreach (var kvp in headers)
                {
                    forwardArgs.Add("--http-header");
                    forwardArgs.Add($"{kvp.Key}={kvp.Value}");
                }
            }
        }

        if (forwardArgs.Count > 0 && PlayerProcessBridge.TryForward(forwardArgs.ToArray()))
        {
            if (!string.IsNullOrWhiteSpace(titleRu) || !string.IsNullOrWhiteSpace(titleEn) || animeId.HasValue || !string.IsNullOrWhiteSpace(torrentHash))
            {
                PlayerProcessBridge.ForwardMetadata(
                    originalTitle: canonicalOriginal,
                    animeId: animeId ?? 0,
                    titleRu: titleRu,
                    titleEn: titleEn,
                    titleRomaji: titleEn,
                    episodeText: episode,
                    torrentHash: torrentHash);
            }
            return;
        }

        // 2. Otherwise launch a standalone player process
        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(processPath)) return;

        var startInfo = CreatePlayerStartInfo(
            processPath,
            assemblyPath,
            videoUrl,
            titleRu,
            titleEn,
            episode,
            animeId,
            torrentHash,
            canonicalOriginal,
            subtitleUrl,
            headers);

        try
        {
            Process.StartAndForget(startInfo);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to launch player process");
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

        return url.Contains(Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.DefaultHost, StringComparison.OrdinalIgnoreCase) ||
               url.Contains("/stream", StringComparison.OrdinalIgnoreCase) ||
               System.Text.RegularExpressions.Regex.IsMatch(url, Kiriha.Core.Domain.Constants.AppConstants.Torrents.TorrentHashLinkRegex);
    }
}
