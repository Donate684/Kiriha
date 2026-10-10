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
        string? originalTitle = null)
    {
        var canonicalOriginal = !string.IsNullOrWhiteSpace(originalTitle)
            ? originalTitle
            : (titleEn ?? titleRu ?? string.Empty);

        // 1. If resident player instance is running, forward playback and metadata through pipe
        if (!string.IsNullOrWhiteSpace(videoUrl) && PlayerProcessBridge.TryForward(["--player", videoUrl]))
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

        var isDotnet = System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var argsBuilder = new StringBuilder();
        if (isDotnet)
        {
            argsBuilder.Append($"\"{assemblyPath}\" ");
        }
        argsBuilder.Append("--player");

        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            argsBuilder.Append($" \"{videoUrl}\"");
        }
        if (!string.IsNullOrWhiteSpace(canonicalOriginal))
        {
            argsBuilder.Append($" --original-title \"{canonicalOriginal}\"");
        }
        if (!string.IsNullOrWhiteSpace(titleRu))
        {
            argsBuilder.Append($" --title-ru \"{titleRu}\"");
        }
        if (!string.IsNullOrWhiteSpace(titleEn))
        {
            argsBuilder.Append($" --title-en \"{titleEn}\"");
        }
        if (!string.IsNullOrWhiteSpace(episode))
        {
            argsBuilder.Append($" --episode \"{episode}\"");
        }
        if (animeId.HasValue)
        {
            argsBuilder.Append($" --anime-id {animeId.Value}");
        }
        if (!string.IsNullOrWhiteSpace(torrentHash))
        {
            argsBuilder.Append($" --torrent-hash \"{torrentHash}\"");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = argsBuilder.ToString(),
            UseShellExecute = false,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.KillOnParentExit = true;
        }

        try
        {
            Process.StartAndForget(startInfo);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to launch player process");
        }
    }
}
