using System.Collections.Concurrent;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Tracking.Api;

/// <summary>
/// Shared in-memory lookup state across all <see cref="ShikiApiService"/> instances
/// (dynamic <c>IShikiApiService</c> and mirror-pinned <c>ITrackerService</c> instances).
/// Ensures MAL↔Shikimori ID mappings, GraphQL poster URLs, and account-scoped user rate IDs
/// are populated once and visible to every Shikimori consumer without duplication.
/// </summary>
public sealed class ShikiStateCache
{
    public ConcurrentDictionary<(int PrimaryId, string TargetType), int> MalToShikiMap { get; } = new();

    public ConcurrentDictionary<string, string> PosterMemoryCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<(ShikiMirror Mirror, int UserId, string TargetType, int TargetId), int> _userRateMap = new();

    public void SetUserRateId(ShikiMirror mirror, int userId, string targetType, int targetId, int rateId)
    {
        _userRateMap[(mirror, userId, targetType.ToLowerInvariant(), targetId)] = rateId;
    }

    public bool TryGetUserRateId(ShikiMirror mirror, int userId, string targetType, int targetId, out int rateId)
    {
        return _userRateMap.TryGetValue((mirror, userId, targetType.ToLowerInvariant(), targetId), out rateId);
    }

    public void RemoveUserRateId(ShikiMirror mirror, int userId, string targetType, int targetId)
    {
        _userRateMap.TryRemove((mirror, userId, targetType.ToLowerInvariant(), targetId), out _);
    }
}
