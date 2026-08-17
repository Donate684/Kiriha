using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Mpv;

internal sealed class MpvEventLoop : IDisposable
{
    private readonly object _gate;
    private readonly Func<IntPtr> _getHandle;
    private readonly Action<MpvEvent> _handleEvent;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public MpvEventLoop(object gate, Func<IntPtr> getHandle, Action<MpvEvent> handleEvent)
    {
        _gate = gate;
        _getHandle = getHandle;
        _handleEvent = handleEvent;
    }

    public void Start()
    {
        _loopTask = Task.Run(EventLoop);
    }

    public bool Stop(IntPtr handle, TimeSpan timeout)
    {
        _cts.Cancel();

        if (handle != IntPtr.Zero)
            LibMpvNative.mpv_wakeup(handle);

        return MpvTask.Wait(_loopTask, timeout, "mpv event loop");
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    private void EventLoop()
    {
        IntPtr handle;
        lock (_gate)
        {
            handle = _getHandle();
        }

        if (handle == IntPtr.Zero)
            return;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var eventPtr = LibMpvNative.mpv_wait_event(handle, 0.5);
                if (eventPtr == IntPtr.Zero)
                    continue;

                MpvEvent mpvEvent;
                unsafe { mpvEvent = *(MpvEvent*)eventPtr; }
                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_NONE)
                    continue;

                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_SHUTDOWN)
                    break;

                _handleEvent(mpvEvent);
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Log.Warning(ex, "MpvPlayer event loop failed");
            }
        }
    }
}
