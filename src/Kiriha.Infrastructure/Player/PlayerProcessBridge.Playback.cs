using System.IO.Pipes;
using System.Text;
using Serilog;

namespace Kiriha.Infrastructure.Player;

public static partial class PlayerProcessBridge
{
    private static readonly TimeSpan PlaybackExchangeTimeout = TimeSpan.FromSeconds(1);

    public static Task<PlayerLaunchStatus> ExchangePlaybackAsync(string[] command, CancellationToken ct = default) =>
        ExchangePlaybackAsync(PipeName, command, PlaybackExchangeTimeout, ct);

    internal static async Task<PlayerLaunchStatus> ExchangePlaybackAsync(
        string pipeName, string[] command, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        bool connected = false;
        try
        {
            await client.ConnectAsync(token).ConfigureAwait(false);
            connected = true;
            var frame = Encoding.UTF8.GetBytes(PipeArgumentSerializer.Serialize(command) + "\n");
            await client.WriteAsync(frame.AsMemory(), token).ConfigureAwait(false);
            var response = new byte[1];
            await client.ReadExactlyAsync(response, token).ConfigureAwait(false);
            return response[0] switch
            {
                (byte)PlayerLaunchStatus.Accepted => PlayerLaunchStatus.Accepted,
                (byte)PlayerLaunchStatus.Ready => PlayerLaunchStatus.Ready,
                (byte)PlayerLaunchStatus.Failed => PlayerLaunchStatus.Failed,
                (byte)PlayerLaunchStatus.Superseded => PlayerLaunchStatus.Superseded,
                (byte)PlayerLaunchStatus.NotFound => PlayerLaunchStatus.NotFound,
                _ => PlayerLaunchStatus.Unconfirmed
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return connected ? PlayerLaunchStatus.Unconfirmed : PlayerLaunchStatus.Unavailable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ct.ThrowIfCancellationRequested();
            Log.Debug(ex, "Player playback command could not be confirmed");
            return connected ? PlayerLaunchStatus.Unconfirmed : PlayerLaunchStatus.Unavailable;
        }
    }
}
