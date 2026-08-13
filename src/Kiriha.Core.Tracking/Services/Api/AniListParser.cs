using System;
using System.Text.Json;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Tracking.Api;

public static class AniListParser
{
    public static AniListAiringInfo? ParseAiringInfo(JsonElement root, int requestedMalId)
    {
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Media", out var media)
            || media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var status = media.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;

        int? episode = null;
        DateTime? nextAt = null;

        if (media.TryGetProperty("nextAiringEpisode", out var next) && next.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            if (next.TryGetProperty("episode", out var epElement) && epElement.ValueKind == JsonValueKind.Number && epElement.TryGetInt32(out var ep) && ep > 0)
                episode = ep;

            if (next.TryGetProperty("airingAt", out var atElement) && atElement.ValueKind == JsonValueKind.Number && atElement.TryGetInt64(out var airingAt) && airingAt > 0)
                nextAt = DateTimeOffset.FromUnixTimeSeconds(airingAt).UtcDateTime;
        }

        var aniListId = media.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var id) ? id : 0;
        var malId = media.TryGetProperty("idMal", out var malElement) && malElement.ValueKind == JsonValueKind.Number && malElement.TryGetInt32(out var parsedMalId)
            ? parsedMalId
            : requestedMalId;
        var totalEpisodes = media.TryGetProperty("episodes", out var epsElement) && epsElement.ValueKind == JsonValueKind.Number && epsElement.TryGetInt32(out var eps)
            ? eps
            : (int?)null;

        return new AniListAiringInfo(aniListId, malId, status, episode, nextAt, totalEpisodes);
    }
}
