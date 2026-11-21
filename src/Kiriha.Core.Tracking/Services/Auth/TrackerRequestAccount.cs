using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Tracking.Auth;

/// <summary>Pins credentials across the requests and refreshes of a single sync operation.</summary>
internal sealed class TrackerRequestAccount : IDisposable
{
    private static readonly AsyncLocal<TrackerAccount?> Current = new();
    private readonly TrackerAccount? _previous;

    public TrackerRequestAccount(TrackerAccount? account)
    {
        _previous = Current.Value;
        Current.Value = account?.Clone();
    }

    public static TrackerAccount? Pinned(string trackerId) =>
        Current.Value is { } account && string.Equals(account.TrackerId, trackerId, StringComparison.OrdinalIgnoreCase)
            ? account
            : null;

    public static TrackerAccount? Get(ISettingsService settings, string trackerId) =>
        Pinned(trackerId) ?? settings.Current.Api.GetAccount(trackerId);

    public void Dispose() => Current.Value = _previous;
}
