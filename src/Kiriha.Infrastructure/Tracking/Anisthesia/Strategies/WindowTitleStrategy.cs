using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;

namespace Kiriha.Infrastructure.Tracking.Anisthesia.Strategies;

public class WindowTitleStrategy
{
    public static ParsedMedia? Apply(AnisthesiaPlayer player, uint pid, IntPtr hWnd)
    {
        string windowTitle = Win32Api.GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(windowTitle)) return null;

        if (string.IsNullOrEmpty(player.WindowTitleFormat))
        {
            // If no format specified, but window is detected, return title as is
            return new ParsedMedia { OriginalTitle = windowTitle, AnimeTitle = windowTitle, IsPlaying = true };
        }

        try
        {
            var match = Regex.Match(windowTitle, player.WindowTitleFormat);
            if (match.Success)
            {
                bool groupMatched = false;
                // Find first non-empty group (skipping the full match)
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
                    {
                        string extracted = match.Groups[i].Value;

                        // Further parse with AnimeReleaseParser to get episode
                        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(extracted);
                        string animeTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : extracted;

                        return new ParsedMedia
                        {
                            OriginalTitle = extracted,
                            AnimeTitle = animeTitle,
                            EpisodeTitle = parsed.EpisodeTitle,
                            Episode = parsed.Episode,
                            Season = parsed.Season?.ToString() ?? string.Empty,
                            IsPlaying = true
                        };
                    }
                }

                // If there were capturing groups defined in regex, but none matched, 
                // it means the "idle" part of the regex matched (e.g. "^MPC-BE.*")
                // In this case we return null because nothing is actually playing.
                if (player.WindowTitleFormat.Contains("(") && !groupMatched)
                {
                    return null;
                }

                // If no capturing groups in regex at all, we take the whole match
                return new ParsedMedia { OriginalTitle = windowTitle, AnimeTitle = windowTitle, IsPlaying = true };
            }
        }
        catch
        {
            // Invalid regex or other error
        }

        return null;
    }
}

