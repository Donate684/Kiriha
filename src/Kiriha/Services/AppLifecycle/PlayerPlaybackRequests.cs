using Kiriha.Infrastructure.Player;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

/// <summary>Remembers per-request results so a lost acknowledgment cannot open the same media twice.</summary>
internal sealed class PlayerPlaybackRequests
{
    private const int MaxRequests = 256;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, Entry> _requests = new(StringComparer.Ordinal);

    internal sealed record Target(Func<PlayerLaunchStatus> Read, Action Cancel);
    private sealed record Entry(Target Target, DateTimeOffset ExpiresAt);

    internal PlayerLaunchStatus Handle(string[] frame, bool closing, Func<string[], string, Target> open)
    {
        if (frame.Length < 2 || !PlayerPlaybackProtocol.IsValidRequestId(frame[1]))
            return PlayerLaunchStatus.Failed;

        var requestId = frame[1];
        if (frame is [PlayerPlaybackProtocol.Query, _])
            return Get(requestId);
        if (frame is [PlayerPlaybackProtocol.Cancel, _])
            return Cancel(requestId);
        if (closing || frame is not [PlayerPlaybackProtocol.Open, _, { } payload])
            return PlayerLaunchStatus.Failed;

        var args = PipeArgumentSerializer.Deserialize(payload);
        if (args.Any(arg => arg == null) || !PlayerModeCoordinator.IsPlayerMode(args) || PlayerProcessBridge.IsResident(args) ||
            PlayerModeCoordinator.GetPlaybackRequestId(args) != requestId)
            return PlayerLaunchStatus.Failed;

        return Open(requestId, IsExpired(args), () => open(args, requestId));
    }

    internal static bool IsExpired(string[] args) =>
        PlayerPlaybackProtocol.IsAdmissionExpired(PlayerModeCoordinator.GetArgValue(args, PlayerPlaybackProtocol.AdmissionDeadlineArg));

    // Requests and window callbacks are serialized by the UI dispatcher.
    internal PlayerLaunchStatus Open(string id, bool expired, Func<Target> open)
    {
        if (_requests.TryGetValue(id, out var existing))
            return existing.Target.Read();
        if (expired || !CanAdd())
            return PlayerLaunchStatus.Failed;

        Target target;
        try
        {
            target = open();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not accept player request {RequestId}", id);
            target = Terminal(PlayerLaunchStatus.Failed);
        }

        _requests.Add(id, new Entry(target, DateTimeOffset.UtcNow + Retention));
        return target.Read();
    }

    internal PlayerLaunchStatus Get(string id) =>
        _requests.TryGetValue(id, out var entry) ? entry.Target.Read() : PlayerLaunchStatus.NotFound;

    internal PlayerLaunchStatus Cancel(string id)
    {
        if (_requests.TryGetValue(id, out var entry))
        {
            var status = entry.Target.Read();
            if (status != PlayerLaunchStatus.Accepted)
                return status;

            RememberCancelled(id);
            entry.Target.Cancel();
        }
        else if (CanAdd())
        {
            RememberCancelled(id);
        }
        return PlayerLaunchStatus.Superseded;
    }

    private void RememberCancelled(string id) =>
        _requests[id] = new Entry(Terminal(PlayerLaunchStatus.Superseded), DateTimeOffset.UtcNow + Retention);

    private static Target Terminal(PlayerLaunchStatus status) => new(() => status, () => { });

    private bool CanAdd()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _requests.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            _requests.Remove(key);
        return _requests.Count < MaxRequests;
    }
}
