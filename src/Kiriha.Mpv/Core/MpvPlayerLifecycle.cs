using Serilog;

namespace Kiriha.Mpv;

internal static class MpvPlayerLifecycle
{
    public static bool Dispose(
        IntPtr handle,
        MpvCommandQueue commandQueue,
        MpvEventLoop eventLoop,
        Action<IntPtr> unobservePlaybackProperties)
    {
        if (handle == IntPtr.Zero)
        {
            eventLoop.Dispose();
            return true;
        }

        var commandStopped = commandQueue.WaitForStop(TimeSpan.FromSeconds(3));
        if (!commandStopped)
            Log.Warning("Timed out waiting for mpv command loop to stop");

        var eventsStopped = eventLoop.Stop(handle, TimeSpan.FromSeconds(3));
        if (!eventsStopped)
            Log.Warning("Timed out waiting for mpv event loop to stop");

        // Native callbacks and commands may still hold the handle if stopping a loop timed out.
        if (!commandStopped || !eventsStopped)
        {
            _ = ReleaseWhenStoppedAsync(handle, commandQueue, eventLoop, unobservePlaybackProperties);
            return false;
        }

        return Release(handle, eventLoop, unobservePlaybackProperties);
    }

    private static async Task ReleaseWhenStoppedAsync(IntPtr handle, MpvCommandQueue commands,
        MpvEventLoop events, Action<IntPtr> unobservePlaybackProperties)
    {
        await Task.WhenAll(commands.Completion, events.Completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Release(handle, events, unobservePlaybackProperties);
    }

    private static bool Release(IntPtr handle, MpvEventLoop eventLoop, Action<IntPtr> unobservePlaybackProperties)
    {
        try
        {
            unobservePlaybackProperties(handle);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unobserve mpv properties");
        }
        var terminated = Terminate(handle);
        eventLoop.Dispose();
        return terminated;
    }

    private static bool Terminate(IntPtr handle)
    {
        var terminateTask = Task.Run(() =>
        {
            try
            {
                LibMpvNative.mpv_terminate_destroy(handle);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to terminate mpv");
                return false;
            }
        });

        if (!MpvTask.Wait(terminateTask, TimeSpan.FromSeconds(2), "mpv termination"))
        {
            Log.Warning("Timed out waiting for mpv to terminate");
            return false;
        }
        return terminateTask.IsCompletedSuccessfully && terminateTask.Result;
    }
}
