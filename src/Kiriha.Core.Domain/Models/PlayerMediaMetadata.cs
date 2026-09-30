namespace Kiriha.Core.Domain.Models;

public sealed record PlayerMediaMetadata(
    string OriginalTitle,
    string TitleRu,
    string TitleEn,
    string EpisodeText,
    int? AnimeId,
    string TitleRomaji = "")
{
    public PlayerMediaMetadata() : this(string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty) { }

    public static PlayerMediaMetadata FromVideoPath(string videoPath)
    {
        var fallbackTitle = string.IsNullOrWhiteSpace(videoPath)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(videoPath);

        return new PlayerMediaMetadata(fallbackTitle, fallbackTitle, string.Empty, string.Empty, null, fallbackTitle);
    }
}
