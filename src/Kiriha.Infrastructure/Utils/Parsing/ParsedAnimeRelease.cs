namespace Kiriha.Utils.Parsing;

/// <summary>
/// Результат структурного разбора имени файла, торрент-релиза или заголовка окна плеера.
/// </summary>
public sealed record ParsedAnimeRelease
{
    public static readonly ParsedAnimeRelease Empty = new();

    /// <summary>Очищенное название аниме (без группы, номера серии, разрешения и тех. тегов).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Номер серии в строковом виде (например, "01", "12", "05.5", "01-12" для батчей) или пустая строка.</summary>
    public string Episode { get; init; } = string.Empty;

    /// <summary>Номер сезона (например, 2 для S02E05 / Season 2 / 2nd Season / ТВ-2), если явно указан в релизе.</summary>
    public int? Season { get; init; }

    /// <summary>Релиз-группа или студия озвучки (например, "SubsPlease", "Erai-raws", "AniLibria.TV").</summary>
    public string ReleaseGroup { get; init; } = string.Empty;

    /// <summary>Разрешение видео в нормализованном виде (например, "1080p", "720p", "2160p", "480p").</summary>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>Тип релиза, если обнаружен ("OVA", "ONA", "Movie", "Special", "OAD", "SP").</summary>
    public string ReleaseType { get; init; } = string.Empty;

    /// <summary>Название эпизода или остаточный подзаголовок после номера серии (если есть).</summary>
    public string EpisodeTitle { get; init; } = string.Empty;

    /// <summary>Признак батча / полного сезона (Batch, Complete, диапазоны 01-12 и т.д.).</summary>
    public bool IsBatch { get; init; }
}
