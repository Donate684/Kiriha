using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Parsing;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public interface IPlayerMediaMetadataResolver
{
    PlayerMediaMetadata Resolve(string videoPath);
    PlayerMediaMetadata? ResolveByTitle(string title, string? episodeText = null) => null;
}

public sealed partial class PlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    [System.Text.RegularExpressions.GeneratedRegex(@"([sS]?\d*[eE]\d+)\s*-(.*)")]
    private static partial System.Text.RegularExpressions.Regex HyphenatedEpisodeRegex();

    public PlayerMediaMetadataResolver(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            string filename;
            if (Uri.TryCreate(videoPath, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var seg = uri.Segments.LastOrDefault() ?? string.Empty;
                filename = Uri.UnescapeDataString(System.IO.Path.GetFileNameWithoutExtension(seg));
            }
            else
            {
                filename = System.IO.Path.GetFileNameWithoutExtension(videoPath);
            }

            if (PlayerMediaMetadata.IsStreamManifest(filename))
                return new PlayerMediaMetadata();

            var filenameToParse = HyphenatedEpisodeRegex().Replace(filename, "$1 - $2");
            var parsed = AnimeParseCache.Parse(filenameToParse);

            var extractedTitle = parsed.Title;
            var episodeText = parsed.Episode;
            int parsedSeason = parsed.Season ?? 0;

            string originalTitle = filename;
            bool isEmber = videoPath.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(videoPath);
            if (isEmber)
            {
                string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(meaningfulDir))
                {
                    var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                    extractedTitle = !string.IsNullOrEmpty(dirParsed.Title) ? dirParsed.Title : meaningfulDir;
                    originalTitle = meaningfulDir;
                }
            }

            if (string.IsNullOrWhiteSpace(extractedTitle))
                return PlayerMediaMetadata.FromVideoPath(videoPath) with { EpisodeText = episodeText };

            var searchTitle = parsedSeason > 1
                ? $"{extractedTitle} Season {parsedSeason}"
                : extractedTitle;

            using var db = _dbFactory.CreateDbContext();

            var searchTitleLower = searchTitle.ToLower();
            var match = db.UserAnime.AsNoTracking().FirstOrDefault(a =>
                (a.RussianTitle != null && a.RussianTitle.ToLower() == searchTitleLower) ||
                (a.EnglishTitle != null && a.EnglishTitle.ToLower() == searchTitleLower) ||
                (a.Title != null && a.Title.ToLower() == searchTitleLower));

            if (match is null && parsedSeason <= 1)
            {
                var extractedTitleLower = extractedTitle.ToLower();
                match = db.UserAnime.AsNoTracking().FirstOrDefault(a =>
                    (a.RussianTitle != null && a.RussianTitle.ToLower() == extractedTitleLower) ||
                    (a.EnglishTitle != null && a.EnglishTitle.ToLower() == extractedTitleLower) ||
                    (a.Title != null && a.Title.ToLower() == extractedTitleLower));
            }

            return match is null
                ? new PlayerMediaMetadata(originalTitle, extractedTitle, string.Empty, episodeText, null, extractedTitle)
                : new PlayerMediaMetadata(
                    originalTitle,
                    match.RussianTitle ?? match.Title ?? extractedTitle,
                    match.EnglishTitle ?? match.Title ?? string.Empty,
                    episodeText,
                    match.Id,
                    match.Title ?? extractedTitle);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve player media metadata for {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }

    public PlayerMediaMetadata? ResolveByTitle(string title, string? episodeText = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var cleanTitle = title.Trim();
            var searchLower = cleanTitle.ToLower();

            var match = db.UserAnime.AsNoTracking().FirstOrDefault(a =>
                (a.RussianTitle != null && a.RussianTitle.ToLower() == searchLower) ||
                (a.EnglishTitle != null && a.EnglishTitle.ToLower() == searchLower) ||
                (a.Title != null && a.Title.ToLower() == searchLower));

            if (match == null)
            {
                var norm = new string(cleanTitle.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (!string.IsNullOrEmpty(norm))
                {
                    match = db.UserAnime.AsNoTracking().AsEnumerable().FirstOrDefault(a =>
                        (a.RussianTitle != null && new string(a.RussianTitle.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() == norm) ||
                        (a.EnglishTitle != null && new string(a.EnglishTitle.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() == norm) ||
                        (!string.IsNullOrEmpty(a.Title) && new string(a.Title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() == norm));
                }
            }

            if (match != null)
            {
                return new PlayerMediaMetadata(
                    cleanTitle,
                    match.RussianTitle ?? match.Title ?? cleanTitle,
                    match.EnglishTitle ?? match.Title ?? string.Empty,
                    episodeText ?? string.Empty,
                    match.Id,
                    match.Title ?? cleanTitle);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve metadata by title for '{Title}'", title);
        }

        return null;
    }
}
