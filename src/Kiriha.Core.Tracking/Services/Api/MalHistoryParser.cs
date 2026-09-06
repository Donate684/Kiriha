using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Constants;

namespace Kiriha.Core.Tracking.Services.Api;

public sealed record MalHistoryEntry(int Episode, DateTime WatchedAt);

public sealed record MalHistorySession(int SessionIndex, DateTime StartDate, DateTime EndDate, List<MalHistoryEntry> Entries);

public sealed record MalParsedHistoryResult(DateTime? LatestStartDate, DateTime? LatestEndDate, int TotalSessions, List<MalHistorySession> Sessions);

public static partial class MalHistoryParser
{
    // Regex matching:
    // Anime: "Ep 26, watched on 07/31/2022 at 22:51"
    // Manga: "Chapter 233, read on 04/16/2013 at 09:21" or "Chap 1, read on 01/01/2020"
    [GeneratedRegex(@"(?:Ep|Chap(?:ter)?)\s*(?<ep>\d+)[,\s]+(?:watched|read)\s+on\s+(?<month>\d{1,2})/(?<day>\d{1,2})/(?<year>\d{4})(?:\s+at\s+(?<time>\d{1,2}:\d{2}))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeEntryRegex();

    public static MalParsedHistoryResult? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Contains(AppConstants.Api.Mal.NotLoggedIn, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var matches = EpisodeEntryRegex().Matches(html);
        if (matches.Count == 0)
        {
            return null;
        }

        var rawEntries = new List<MalHistoryEntry>(matches.Count);
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups["ep"].Value, out var ep) &&
                int.TryParse(match.Groups["month"].Value, out var month) &&
                int.TryParse(match.Groups["day"].Value, out var day) &&
                int.TryParse(match.Groups["year"].Value, out var year))
            {
                var timeStr = match.Groups["time"].Value;
                int hour = 0, minute = 0;
                if (!string.IsNullOrEmpty(timeStr) && TimeSpan.TryParse(timeStr, out var ts))
                {
                    hour = ts.Hours;
                    minute = ts.Minutes;
                }

                try
                {
                    var dt = new DateTime(year, month, day, hour, minute, 0);
                    rawEntries.Add(new MalHistoryEntry(ep, dt));
                }
                catch
                {
                    // Ignore invalid calendar dates
                }
            }
        }

        if (rawEntries.Count == 0)
        {
            return null;
        }

        // Divide into watch sessions.
        // In MAL's ajaxtb.php, the log is listed top-to-bottom in reverse chronological order:
        // Top is the most recently watched episode (e.g. Ep 26, 25... 1 of the rewatch).
        // When episode number jumps UP (e.g., from 1 to 26, or current.Episode >= prev.Episode),
        // that marks the boundary to an earlier watch session!
        var sessions = new List<MalHistorySession>();
        var currentSessionEntries = new List<MalHistoryEntry>();

        for (int i = 0; i < rawEntries.Count; i++)
        {
            var entry = rawEntries[i];
            if (currentSessionEntries.Count > 0)
            {
                var prevEntry = currentSessionEntries[^1];
                // In MAL history, entries are listed in reverse chronological order (top is latest).
                // A true rewatch boundary occurs when:
                // 1) The user completed/started a run (ep <= 3) and it jumps to a much higher episode (epJump >= 3), OR
                // 2) The episode jumps up and there is a significant date gap (> 14 days).
                // Minor episode jitter on the same day (e.g. ep 4 at 22:08 and ep 5 at 22:07) stays within the same session.
                var epJump = entry.Episode - prevEntry.Episode;
                var daysDiff = (prevEntry.WatchedAt - entry.WatchedAt).TotalDays;
                bool isRewatchBoundary = (epJump >= 3 && (prevEntry.Episode <= 3 || daysDiff > 14)) ||
                                         (epJump > 0 && daysDiff > 60);

                if (isRewatchBoundary)
                {
                    // Previous session boundary detected
                    sessions.Add(CreateSession(sessions.Count + 1, currentSessionEntries));
                    currentSessionEntries = new List<MalHistoryEntry>();
                }
            }
            currentSessionEntries.Add(entry);
        }

        if (currentSessionEntries.Count > 0)
        {
            sessions.Add(CreateSession(sessions.Count + 1, currentSessionEntries));
        }

        if (sessions.Count == 0) return null;

        // The LATEST session is sessions[0] (the top one in MAL's log)
        var latestSession = sessions[0];
        return new MalParsedHistoryResult(
            LatestStartDate: latestSession.StartDate,
            LatestEndDate: latestSession.EndDate,
            TotalSessions: sessions.Count,
            Sessions: sessions);
    }

    private static MalHistorySession CreateSession(int index, List<MalHistoryEntry> entries)
    {
        DateTime minDate = DateTime.MaxValue;
        DateTime maxDate = DateTime.MinValue;
        DateTime? ep1Date = null;
        int maxEp = -1;
        DateTime? maxEpDate = null;

        foreach (var e in entries)
        {
            if (e.WatchedAt < minDate) minDate = e.WatchedAt;
            if (e.WatchedAt > maxDate) maxDate = e.WatchedAt;

            if (e.Episode == 1) ep1Date = e.WatchedAt;
            if (e.Episode > maxEp)
            {
                maxEp = e.Episode;
                maxEpDate = e.WatchedAt;
            }
        }

        var startDate = ep1Date?.Date ?? minDate.Date;
        var endDate = maxEpDate?.Date ?? maxDate.Date;

        return new MalHistorySession(index, startDate, endDate, entries);
    }
}
