using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Platform;
using Kiriha.Infrastructure.Tracking.ExternalDetection;

namespace Kiriha.Infrastructure.Tracking.ExternalDetection.Strategies;

public static class WindowTitleStrategy
{
    public static ParsedMedia? Apply(ExternalPlayerDefinition player, uint pid, IntPtr hWnd, Regex? compiledFormat = null)
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
            var match = compiledFormat != null
                ? compiledFormat.Match(windowTitle)
                : Regex.Match(windowTitle, player.WindowTitleFormat);

            if (match.Success)
            {
                // Find first non-empty group (skipping the full match)
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
                    {
                        return MediaTitleResolver.FromExtractedTitle(match.Groups[i].Value);
                    }
                }

                // If there were capturing groups defined in regex, but none matched, 
                // it means the "idle" part of the regex matched (e.g. "^MPC-BE.*")
                // In this case we return null because nothing is actually playing.
                if (player.WindowTitleFormat.Contains('('))
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
