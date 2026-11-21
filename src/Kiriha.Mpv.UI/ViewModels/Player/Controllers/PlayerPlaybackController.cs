namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed class PlayerPlaybackController
{
    private MpvPlayer? _player;

    public event Action<MpvSourceEvent>? SourceEvent;

    public bool HasPlayer => _player != null;

    public void Attach(MpvPlayer player)
    {
        Detach();

        _player = player;
        _player.SourceEvent += OnSourceEvent;
    }

    public void Detach()
    {
        if (_player is null)
            return;

        _player.SourceEvent -= OnSourceEvent;
        _player = null;
    }

    public long LoadSource(string videoUrl, IReadOnlyDictionary<string, string>? headers, bool autoPlay) =>
        _player?.LoadSource(videoUrl, headers, autoPlay) ?? 0;
    public void WriteWatchLaterConfig() => _player?.WriteWatchLaterConfig();
    public void DeleteWatchLaterConfig() => _player?.DeleteWatchLaterConfig();
    public void SetSavePositionOnQuit(bool enabled) => _player?.SetSavePositionOnQuit(enabled);
    public void AddSubtitle(string path, bool select = true) => _player?.AddSubtitle(path, select);
    public void AddAudioTrack(string path) => _player?.AddAudioTrack(path);
    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public void Seek(double timeInSeconds, bool fast = false) => _player?.Seek(timeInSeconds, fast);
    public void SetVolume(double volume) => _player?.SetVolume(volume);
    public void SetSpeed(double speed) => _player?.SetSpeed(speed);
    public void SetAudioNormalization(bool enabled) => _player?.SetAudioNormalization(enabled);
    public void CycleSubtitle() => _player?.CycleSubtitle();
    public void CycleAudio() => _player?.CycleAudio();
    public void ReloadSubtitles() => _player?.ReloadSubtitles();
    public void FrameStep() => _player?.FrameStep();
    public void FrameBackStep() => _player?.FrameBackStep();
    public void AdjustSubtitlePosition(double delta) => _player?.AdjustSubtitlePosition(delta);
    public void AdjustSubtitleDelay(double delta) => _player?.AdjustSubtitleDelay(delta);
    public double GetSubtitleDelay() => _player?.GetSubtitleDelay() ?? 0;
    public void TakeScreenshot(bool includeSubtitles, string resolutionMode) => _player?.TakeScreenshot(includeSubtitles, resolutionMode);
    public void SetTrack(string type, string id) => _player?.SetTrack(type, id);
    public void SetOptionString(string name, string value) => _player?.SetOptionString(name, value);
    public double GetDuration() => _player?.GetDuration() ?? 0;
    public PlaybackState GetPlaybackState() => _player?.GetPlaybackState() ?? new PlaybackState(0, 0, false, false, false);
    public MpvRuntimeDiagnostics GetRuntimeVideoInfo() => _player?.GetRuntimeVideoInfo() ?? new();
    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        _player?.SetScreenshotOptions(directory, format, pngCompression, quality, highBitDepth);
    }

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages)
    {
        _player?.SetTrackLanguagePreferences(audioLanguages, subtitleLanguages);
    }

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        _player?.SetVideoProcessingOptions(
            scale,
            chromaScale,
            ditherDepth,
            correctDownscaling,
            deband,
            debandIterations,
            debandThreshold);
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
        _player?.SetSubtitleStyleOverride(
            enabled,
            font,
            fontSize,
            color,
            borderColor,
            shadowColor,
            borderSize,
            shadowOffset,
            alignY,
            alignX,
            marginY,
            scaleByWindow);
    }

    public async Task<(List<TrackInfo> Tracks, List<ChapterInfo> Chapters)?> GetTracksAndChaptersAsync()
    {
        var player = _player;
        if (player is null)
            return null;

        try
        {
            var result = await Task.Run(() => (Tracks: player.GetTracks(), Chapters: player.GetChapters()));
            return ReferenceEquals(_player, player) ? result : null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to refresh mpv tracks");
            return null;
        }
    }

    private void OnSourceEvent(MpvSourceEvent notification) => SourceEvent?.Invoke(notification);
}
