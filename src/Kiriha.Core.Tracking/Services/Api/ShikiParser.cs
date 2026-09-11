using System;
using System.Globalization;
using System.Text.Json;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Tracking.Api;

public static class ShikiParser
{
    public static EpisodeAiringInfo? ParseAiringInfo(JsonElement root, int requestedMalId)
    {
        if (root.ValueKind is not JsonValueKind.Object)
            return null;

        var malId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var parsedId)
            ? parsedId
            : requestedMalId;

        var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;

        var totalEpisodes = root.TryGetProperty("episodes", out var epsElement) && epsElement.ValueKind == JsonValueKind.Number && epsElement.TryGetInt32(out var eps) && eps > 0
            ? eps
            : (int?)null;

        var episodesAired = root.TryGetProperty("episodes_aired", out var airedElement) && airedElement.ValueKind == JsonValueKind.Number && airedElement.TryGetInt32(out var aired) && aired >= 0
            ? aired
            : (int?)null;

        DateTime? nextAt = null;
        if (root.TryGetProperty("next_episode_at", out var nextElement) && nextElement.ValueKind == JsonValueKind.String)
        {
            var dateStr = nextElement.GetString();
            if (!string.IsNullOrWhiteSpace(dateStr) && DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                nextAt = dto.UtcDateTime;
            }
        }

        var normalizedStatus = status ?? string.Empty;

        if (normalizedStatus.Equals("released", StringComparison.OrdinalIgnoreCase))
        {
            int? finalTotal = totalEpisodes ?? episodesAired;
            return new EpisodeAiringInfo(
                SourceId: malId,
                MalId: requestedMalId,
                Status: "FINISHED",
                NextEpisode: null,
                NextEpisodeAt: null,
                TotalEpisodes: finalTotal,
                EpisodesAired: finalTotal ?? episodesAired);
        }

        if (normalizedStatus.Equals("anons", StringComparison.OrdinalIgnoreCase))
        {
            return new EpisodeAiringInfo(
                SourceId: malId,
                MalId: requestedMalId,
                Status: "NOT_YET_RELEASED",
                NextEpisode: 1,
                NextEpisodeAt: nextAt,
                TotalEpisodes: totalEpisodes,
                EpisodesAired: 0);
        }

        int? nextEpisode = null;
        if (episodesAired.HasValue)
        {
            nextEpisode = episodesAired.Value + 1;
        }
        else if (nextAt.HasValue)
        {
            nextEpisode = 1;
        }

        return new EpisodeAiringInfo(
            SourceId: malId,
            MalId: requestedMalId,
            Status: status,
            NextEpisode: nextEpisode,
            NextEpisodeAt: nextAt,
            TotalEpisodes: totalEpisodes,
            EpisodesAired: episodesAired);
    }
}
