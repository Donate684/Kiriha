using Serilog;

namespace Kiriha.Infrastructure.Player;

/// <summary>Retries admission with the same request ID, then checks readiness without resubmitting playback.</summary>
public sealed class PlayerLaunchClient(
    Func<bool> playerRunning,
    Func<bool> startPlayer,
    Func<string[], CancellationToken, Task<PlayerLaunchStatus>> send,
    TimeSpan? admissionTimeout = null,
    TimeSpan? openingTimeout = null)
{
    private static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan OpeningTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(2);

    public async Task<PlayerLaunchStatus> OpenAsync(string requestId, string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var status = PlayerLaunchStatus.Unconfirmed;
        try
        {
            status = await AdmitAsync(requestId, args, ct).ConfigureAwait(false);
            if (status == PlayerLaunchStatus.Accepted)
                status = await WaitUntilOpenedAsync(requestId, ct).ConfigureAwait(false);
            return status;
        }
        finally
        {
            if (ct.IsCancellationRequested || status == PlayerLaunchStatus.Unconfirmed)
                await CancelPendingAsync(requestId).ConfigureAwait(false);
        }
    }

    private async Task<PlayerLaunchStatus> AdmitAsync(string requestId, string[] args, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(admissionTimeout ?? AdmissionTimeout);
        var token = deadline.Token;
        var command = PlayerPlaybackProtocol.CreateOpen(requestId, args);
        var lastStatus = PlayerLaunchStatus.Unavailable;
        bool started = false;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!started && !playerRunning())
                {
                    if (!startPlayer())
                        return PlayerLaunchStatus.Unavailable;
                    started = true;
                }

                lastStatus = await send(command, token).ConfigureAwait(false);
                if (lastStatus is not (PlayerLaunchStatus.Unavailable or PlayerLaunchStatus.Unconfirmed))
                    return lastStatus;

                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return started || playerRunning() ? PlayerLaunchStatus.Unconfirmed : lastStatus;
        }
    }

    private async Task<PlayerLaunchStatus> WaitUntilOpenedAsync(string requestId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(openingTimeout ?? OpeningTimeout);
        var token = deadline.Token;
        var query = PlayerPlaybackProtocol.CreateQuery(requestId);
        try
        {
            while (true)
            {
                var status = await send(query, token).ConfigureAwait(false);
                if (status is PlayerLaunchStatus.Ready or PlayerLaunchStatus.Failed or PlayerLaunchStatus.Superseded)
                    return status;
                if (status == PlayerLaunchStatus.NotFound || status == PlayerLaunchStatus.Unavailable && !playerRunning())
                    return PlayerLaunchStatus.Failed;

                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PlayerLaunchStatus.Unconfirmed;
        }
    }

    private async Task CancelPendingAsync(string requestId)
    {
        using var deadline = new CancellationTokenSource(CancelTimeout);
        var token = deadline.Token;
        try
        {
            var command = PlayerPlaybackProtocol.CreateCancel(requestId);
            while (true)
            {
                var status = await send(command, token).ConfigureAwait(false);
                if (status is not (PlayerLaunchStatus.Unavailable or PlayerLaunchStatus.Unconfirmed))
                    return;

                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not cancel the pending player request");
        }
    }
}
