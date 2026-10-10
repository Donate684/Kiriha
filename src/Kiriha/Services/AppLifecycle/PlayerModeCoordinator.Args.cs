namespace Kiriha.Services.AppLifecycle;

public sealed partial class PlayerModeCoordinator
{
    public static bool IsPlayerMode(string[] args) =>
        args.Any(arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));

    private static string? GetArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var value = args[index + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    private static string GetPlayerVideoUrl(string[] args)
    {
        var playerArgIndex = Array.FindIndex(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));
        if (playerArgIndex >= 0 && playerArgIndex + 1 < args.Length && !args[playerArgIndex + 1].StartsWith("--"))
            return args[playerArgIndex + 1];

        return string.Empty;
    }

    private static Kiriha.Core.Domain.Models.PlayerMediaMetadata? ExtractMetadataFromArgs(string[] args)
    {
        var titleRu = GetArgValue(args, "--title-ru") ?? string.Empty;
        var titleEn = GetArgValue(args, "--title-en") ?? string.Empty;
        var originalTitle = GetArgValue(args, "--original-title") ?? (string.IsNullOrEmpty(titleEn) ? titleRu : titleEn);
        var episode = GetArgValue(args, "--episode") ?? string.Empty;
        var torrentHash = GetArgValue(args, "--torrent-hash") ?? string.Empty;
        int? animeId = int.TryParse(GetArgValue(args, "--anime-id"), out var id) ? id : null;

        if (string.IsNullOrWhiteSpace(titleRu) && string.IsNullOrWhiteSpace(titleEn) && !animeId.HasValue && string.IsNullOrWhiteSpace(torrentHash))
            return null;

        return new Kiriha.Core.Domain.Models.PlayerMediaMetadata(
            OriginalTitle: originalTitle,
            TitleRu: titleRu,
            TitleEn: titleEn,
            EpisodeText: episode,
            AnimeId: animeId,
            TitleRomaji: string.IsNullOrEmpty(titleEn) ? titleRu : titleEn,
            TorrentHash: torrentHash);
    }
}
