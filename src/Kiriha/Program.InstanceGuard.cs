using Kiriha.Core.Domain.Constants;
using System;
using Kiriha.Core.Player;

namespace Kiriha;

partial class Program
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
            playerMutex = new System.Threading.Mutex(true, Kiriha.Core.Player.PlayerProcessBridge.MutexName, out var playerCreatedNew);
            if (!playerCreatedNew)
            {
                Kiriha.Core.Player.PlayerProcessBridge.TryForward(args);
                return false;
            }
        }

        if (!createdNew)
        {
            try
            {
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", "Kiriha.InstanceServer", System.IO.Pipes.PipeDirection.Out);
                client.Connect(1000);
                using var writer = new System.IO.StreamWriter(client);
                writer.WriteLine(PipeArgumentSerializer.Serialize(args));
            }
            catch (Exception ex) { Console.WriteLine("Failed to forward arguments: " + ex.Message); }

            // Logger is not configured yet - write to console for diagnostics.
            Console.WriteLine("Another instance is already running. Arguments forwarded. Exiting.");
            return false;
        }

        return true;
    }
}
