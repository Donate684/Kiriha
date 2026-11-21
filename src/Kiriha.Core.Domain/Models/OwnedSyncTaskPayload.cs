using System.Globalization;
using System.Text.Json;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Models;

public sealed record SyncTaskOwner(string TrackerId, string AccountKey)
{
    public static SyncTaskOwner FromAccount(TrackerAccount account) => new(
        account.TrackerId.ToLowerInvariant(),
        account.AuthenticatedUserId is > 0 and int userId
            ? "user:" + userId.ToString(CultureInfo.InvariantCulture)
            : "login:" + account.Id);
}

// Owned rows use an envelope in the existing Payload column. Legacy rows contain
// either a bare AnimeEntity or no payload at all.
public sealed record OwnedSyncTaskPayload(SyncTaskOwner Owner, AnimeEntity? Item, bool IsDirect = false, int? HistoryId = null)
{
    public static OwnedSyncTaskPayload? Read(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(nameof(Owner), out _))
                return null;

            var parsed = doc.RootElement.Deserialize<OwnedSyncTaskPayload>();
            return parsed?.Owner is { TrackerId.Length: > 0, AccountKey.Length: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
