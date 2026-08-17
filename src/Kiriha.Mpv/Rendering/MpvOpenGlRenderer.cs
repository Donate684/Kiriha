using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiriha.Mpv;

public class MpvOpenGlRenderer : IDisposable
{
    private readonly MpvPlayer _player;
    private readonly ReaderWriterLockSlim _renderGate = new();
    private IntPtr _renderContext;
    private MpvRenderUpdateCallback? _renderUpdateCallback;
    private GCHandle _renderUpdateHandle;
    private int _disposeState;
    private volatile bool _disposed;

    // This lock is static because it's used within a native callback (OnRenderUpdate) 
    // that lacks an instance context. If multiple instances are created, 
    // they will compete for this shared lock.
    private static readonly object _renderUpdateLock = new();

    public MpvOpenGlRenderer(MpvPlayer player)
    {
        _player = player;
    }

    public void CreateOpenGlRenderContext(MpvOpenGlGetProcAddressCallback getProcAddress)
    {
        lock (_player.Gate)
        {
            if (_player.IsDisposed || _player.MpvHandle == IntPtr.Zero || _renderContext != IntPtr.Zero)
                return;

            var getProcAddressPtr = Marshal.GetFunctionPointerForDelegate(getProcAddress);
            var apiTypePtr = Marshal.StringToCoTaskMemUTF8(LibMpvNative.MPV_RENDER_API_TYPE_OPENGL);

            try
            {
                unsafe
                {
                    var initParams = new MpvOpenGlInitParams(getProcAddressPtr, IntPtr.Zero);
                    
                    var parameters = stackalloc MpvRenderParam[3];
                    parameters[0] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_API_TYPE, apiTypePtr);
                    parameters[1] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, (IntPtr)(&initParams));
                    parameters[2] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_INVALID, IntPtr.Zero);

                    MpvPlayer.Check(LibMpvNative.mpv_render_context_create(out _renderContext, _player.MpvHandle, (IntPtr)parameters), "create OpenGL render context");
                }

                _renderUpdateCallback = OnRenderUpdate;
                _renderUpdateHandle = GCHandle.Alloc(this);
                try
                {
                    LibMpvNative.mpv_render_context_set_update_callback(
                        _renderContext,
                        _renderUpdateCallback,
                        GCHandle.ToIntPtr(_renderUpdateHandle));
                }
                catch
                {
                    _renderUpdateHandle.Free();
                    throw;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(apiTypePtr);
            }
        }
    }

    public void RenderOpenGl(int framebuffer, int width, int height)
    {
        if (_disposed) return;

        try
        {
            _renderGate.EnterReadLock();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed) return;

            IntPtr renderContext;
            lock (_player.Gate)
            {
                renderContext = _renderContext;
            }

            if (renderContext == IntPtr.Zero || width <= 0 || height <= 0)
                return;

            var updateFlags = LibMpvNative.mpv_render_context_update(renderContext);
            if ((updateFlags & LibMpvNative.MPV_RENDER_UPDATE_FRAME) == 0)
                return;

            unsafe
            {
                const int glRgba8 = 0x8058;
                var fbo = new MpvOpenGlFbo(framebuffer, width, height, glRgba8);
                int flipY = 1;

                var parameters = stackalloc MpvRenderParam[3];
                parameters[0] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_OPENGL_FBO, (IntPtr)(&fbo));
                parameters[1] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_FLIP_Y, (IntPtr)(&flipY));
                parameters[2] = new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_INVALID, IntPtr.Zero);

                MpvPlayer.Check(LibMpvNative.mpv_render_context_render(renderContext, (IntPtr)parameters), "render OpenGL frame");
                LibMpvNative.mpv_render_context_report_swap(renderContext);
            }
        }
        finally
        {
            _renderGate.ExitReadLock();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1) return;
        _disposed = true;

        try
        {
            _renderGate.EnterWriteLock();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            IntPtr renderContext;
            lock (_player.Gate)
            {
                renderContext = _renderContext;
                _renderContext = IntPtr.Zero;
            }

            if (renderContext != IntPtr.Zero)
            {
                LibMpvNative.mpv_render_context_set_update_callback(renderContext, null!, IntPtr.Zero);
                LibMpvNative.mpv_render_context_free(renderContext);
            }

            lock (_renderUpdateLock)
            {
                if (_renderUpdateHandle.IsAllocated)
                    _renderUpdateHandle.Free();

                _renderUpdateCallback = null;
            }
        }
        finally
        {
            _renderGate.ExitWriteLock();
            _renderGate.Dispose();
        }
    }


    private static void OnRenderUpdate(IntPtr context)
    {
        if (context == IntPtr.Zero)
            return;

        MpvOpenGlRenderer? renderer = null;
        lock (_renderUpdateLock)
        {
            try
            {
                var handle = GCHandle.FromIntPtr(context);
                if (handle.IsAllocated)
                    renderer = handle.Target as MpvOpenGlRenderer;
            }
            catch (InvalidOperationException)
            {
                // Handle was freed concurrently
            }
        }

        renderer?._player.InvokeRenderUpdateRequested();
    }
}
