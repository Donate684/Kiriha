using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Mpv;

public partial class MpvPlayer : IDisposable
{
    private const ulong TimePositionPropertyId = 1;
    private const ulong DurationPropertyId = 2;
    private const ulong PausePropertyId = 3;
    private const ulong SeekablePropertyId = 4;
    private const ulong IdleActivePropertyId = 5;
    private const ulong TrackListPropertyId = 6;


    private readonly object _gate = new();
    private readonly MpvPropertyCache _propertyCache = new(FormatRuntimeVideoInfo(null, null, null, null, null));
    private readonly MpvCommandQueue _commandQueue;
    private readonly MpvEventLoop _eventLoop;
    private IntPtr _mpvHandle;
    private bool _disposed;

    internal object Gate => _gate;
    internal bool IsDisposed => _disposed;
    internal IntPtr MpvHandle => _mpvHandle;

    public MpvPlaybackController PlaybackController { get; }
    public MpvTrackManager TrackManager { get; }
    public MpvScreenshotManager ScreenshotManager { get; }
    public MpvVideoPipelineConfigurator VideoPipelineConfigurator { get; }
    public MpvOpenGlRenderer OpenGlRenderer { get; }

    public event EventHandler? FileLoaded;
    public event EventHandler<MpvPlaybackEndedEventArgs>? PlaybackEnded;
    public event Action? RenderUpdateRequested;
    public event Action<PlaybackState>? PlaybackStateChanged;
    public event Action? TracksChanged;

    internal MpvPlayer()
    {
        _commandQueue = new MpvCommandQueue(_gate, () => _mpvHandle, () => _disposed);
        _eventLoop = new MpvEventLoop(_gate, () => _mpvHandle, HandleEvent);

        _mpvHandle = LibMpvNative.mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new Exception("Failed to create mpv instance. Ensure mpv-2.dll is in the output mpv/ folder.");

        PlaybackController = new MpvPlaybackController(this);
        TrackManager = new MpvTrackManager(this);
        ScreenshotManager = new MpvScreenshotManager(this);
        VideoPipelineConfigurator = new MpvVideoPipelineConfigurator(this);
        OpenGlRenderer = new MpvOpenGlRenderer(this);
    }

    internal void Initialize()
    {
        ObservePlaybackProperties();
        _commandQueue.Start();
        _eventLoop.Start();
    }

    internal void InvokeRenderUpdateRequested() => RenderUpdateRequested?.Invoke();

    public void CreateOpenGlRenderContext(MpvOpenGlGetProcAddressCallback getProcAddress) => OpenGlRenderer.CreateOpenGlRenderContext(getProcAddress);

    public void RenderOpenGl(int framebuffer, int width, int height) => OpenGlRenderer.RenderOpenGl(framebuffer, width, height);

    public void Load(string url) => PlaybackController.Load(url);

    public void AddSubtitle(string path) => TrackManager.AddSubtitle(path);

    public void Play() => PlaybackController.Play();

    public void Pause() => PlaybackController.Pause();

    public void Seek(double timeInSeconds) => PlaybackController.Seek(timeInSeconds);

    public void SetVolume(double volume) => PlaybackController.SetVolume(volume);

    public void SetSpeed(double speed) => PlaybackController.SetSpeed(speed);

    public void SetAudioNormalization(bool enabled) => PlaybackController.SetAudioNormalization(enabled);

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages) => TrackManager.SetTrackLanguagePreferences(audioLanguages, subtitleLanguages);

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        VideoPipelineConfigurator.SetVideoProcessingOptions(scale, chromaScale, ditherDepth, correctDownscaling, deband, debandIterations, debandThreshold);
    }

    public void SetSubtitleStyleOverride(
        bool enabled,
        string font,
        double fontSize,
        string color,
        string borderColor,
        string shadowColor,
        double borderSize,
        double shadowOffset,
        string alignY,
        string alignX,
        int marginY,
        bool scaleByWindow)
    {
        TrackManager.SetSubtitleStyleOverride(enabled, font, fontSize, color, borderColor, shadowColor, borderSize, shadowOffset, alignY, alignX, marginY, scaleByWindow);
    }

    public void SetOptionString(string name, string value)
    {
        Enqueue(handle =>
        {
            Check(LibMpvNative.mpv_set_property_string(handle, name, value), $"set {name}");
            InvalidateRuntimeVideoInfo();
        });
    }

    public double GetTimePosition()
    {
        return _propertyCache.TimePosition;
    }

    public PlaybackState GetPlaybackState()
    {
        return _propertyCache.PlaybackState;
    }

    public double GetDuration()
    {
        var cached = _propertyCache.Duration;
        if (cached > 0)
            return cached;

        var duration = ReadDoubleProperty("duration", 0);
        if (duration > 0)
            _propertyCache.TryUpdateDuration(duration);

        return duration;
    }

    public bool IsPaused()
    {
        return _propertyCache.IsPaused;
    }

    public void CycleSubtitle() => TrackManager.CycleSubtitle();

    public void AdjustSubtitlePosition(double delta) => TrackManager.AdjustSubtitlePosition(delta);

    public void CycleAudio() => TrackManager.CycleAudio();

    public void ReloadSubtitles() => TrackManager.ReloadSubtitles();

    public void FrameStep() => PlaybackController.FrameStep();

    public void FrameBackStep() => PlaybackController.FrameBackStep();

    public void TakeScreenshot(bool includeSubtitles, string resolutionMode) => ScreenshotManager.TakeScreenshot(includeSubtitles, resolutionMode);

    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        ScreenshotManager.SetScreenshotOptions(directory, format, pngCompression, quality, highBitDepth);
    }

    public string? GetPropertyString(string name)
    {
        return Read(handle => GetPropertyString(handle, name), null);
    }

    public string GetRuntimeVideoInfo()
    {
        if (_propertyCache.HasFreshRuntimeVideoInfo)
            return _propertyCache.RuntimeVideoInfo;

        var info = Read(handle =>
        {
            var hwdec = GetPropertyString(handle, "hwdec-current");
            var interop = GetPropertyString(handle, "hwdec-interop");
            var vo = GetPropertyString(handle, "current-vo") ?? GetPropertyString(handle, "vo-configured");
            var gpuContext = GetPropertyString(handle, "current-gpu-context");
            var decoder = GetPropertyString(handle, "decoder");

            return FormatRuntimeVideoInfo(hwdec, interop, vo, gpuContext, decoder);
        }, _propertyCache.RuntimeVideoInfo);

        _propertyCache.StoreRuntimeVideoInfo(info);
        return _propertyCache.RuntimeVideoInfo;
    }

    public System.Collections.Generic.List<TrackInfo> GetTracks()
    {
        return ReadNodeProperty("track-list", MpvNodeParser.ParseTracks, new List<TrackInfo>());
    }

    public List<ChapterInfo> GetChapters()
    {
        return ReadNodeProperty("chapter-list", MpvNodeParser.ParseChapters, new List<ChapterInfo>());
    }

    public void SetTrack(string type, string id) => TrackManager.SetTrack(type, id);

    public void Dispose()
    {
        IntPtr handle;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _commandQueue.Complete();
            handle = _mpvHandle;
            _mpvHandle = IntPtr.Zero;
        }

        OpenGlRenderer.Dispose();

        Task.Run(() =>
        {
            try
            {
                MpvPlayerLifecycle.Dispose(
                    handle,
                    _commandQueue,
                    _eventLoop,
                    UnobservePlaybackProperties);
            }
            finally
            {
                // Removed _renderGate from MpvPlayer since it moved to OpenGlRenderer
            }
        });
    }

    public void FreeRenderContext() => OpenGlRenderer.Dispose();

    internal void Enqueue(Action<IntPtr> action, string? coalescingKey = null)
    {
        _commandQueue.Enqueue(action, coalescingKey);
    }

    internal void InvalidateRuntimeVideoInfo()
    {
        _propertyCache.InvalidateRuntimeVideoInfo();
    }

}
