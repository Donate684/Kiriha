using Kiriha.Infrastructure.Player;
using Kiriha.Services.AppLifecycle;

namespace Kiriha;

static partial class Program
{
    private static bool TryEnsureSingleInstance(bool isPlayer, string[] args, out System.Threading.Mutex? mutex, out System.Threading.Mutex? playerMutex)
    {
        bool createdNew = true;
        mutex = isPlayer
            ? null
            : new System.Threading.Mutex(true, Kiriha.Core.Domain.Constants.AppConstants.System.MutexName, out createdNew);
        playerMutex = null;

        if (isPlayer)
        {
            playerMutex = new System.Threading.Mutex(true, Kiriha.Infrastructure.Player.PlayerProcessBridge.MutexName, out var playerCreatedNew);
            if (!playerCreatedNew)
            {
                if (ForwardPlayerCommand(args))
                    return false;

                // The existing owner may have exited while handling the request.
                playerMutex.Dispose();
                playerMutex = new System.Threading.Mutex(true, PlayerProcessBridge.MutexName, out playerCreatedNew);
                if (!playerCreatedNew)
                {
                    Environment.ExitCode = 1;
                    return false;
                }
            }
        }

        if (!createdNew)
        {
            try
            {
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", "Kiriha.InstanceServer", System.IO.Pipes.PipeDirection.Out);
                client.Connect(1000);
                using var writer = new System.IO.StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(PipeArgumentSerializer.Serialize(args));
                writer.Flush();
                try { client.WaitForPipeDrain(); } catch { }
            }
            catch (Exception ex) { Console.WriteLine("Failed to forward arguments: " + ex.Message); }

            // Logger is not configured yet - write to console for diagnostics.
            Console.WriteLine("Another instance is already running. Arguments forwarded. Exiting.");
            return false;
        }

        return true;
    }

    private static bool ForwardPlayerCommand(string[] args)
    {
        // Its parent still owns the playback command and will use the existing player's pipe.
        if (args.Contains(PlayerPlaybackProtocol.BootstrapArg, StringComparer.OrdinalIgnoreCase))
            return true;

        if (PlayerProcessBridge.IsResident(args) ||
            args.Contains(PlayerProcessBridge.ShutdownArg, StringComparer.OrdinalIgnoreCase) ||
            args.Contains(PlayerProcessBridge.UpdateMetadataArg, StringComparer.OrdinalIgnoreCase))
        {
            PlayerProcessBridge.TryForward(args);
            return true;
        }

        var id = PlayerModeCoordinator.GetPlaybackRequestId(args);
        if (id == null)
        {
            id = Guid.NewGuid().ToString("N");
            args = [.. args, PlayerProcessBridge.PlaybackRequestArg, id];
        }

        var command = PlayerPlaybackProtocol.CreateOpen(id, args);
        var result = PlayerProcessBridge.ExchangePlaybackAsync(command).GetAwaiter().GetResult();
        if (result == PlayerLaunchStatus.Unconfirmed)
            result = PlayerProcessBridge.ExchangePlaybackAsync(command).GetAwaiter().GetResult();
        if (result is PlayerLaunchStatus.Accepted or PlayerLaunchStatus.Ready)
            return true;

        if (result is PlayerLaunchStatus.Failed or PlayerLaunchStatus.Superseded)
        {
            Environment.ExitCode = 1;
            return true;
        }
        return false;
    }
}
