using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed partial class PlayerOverlayViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };
    private readonly DispatcherTimer _resumePromptTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _skipToastTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _skipPromptTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _skipPromptFadeTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };

    public event Action<SkipSegment>? SkipPromptDismissed;

    [ObservableProperty] private bool _isOsdVisible;
    [ObservableProperty] private string _osdMessage = string.Empty;
    [ObservableProperty] private string _osdDetail = string.Empty;
    [ObservableProperty] private bool _isTimelinePreviewVisible;
    [ObservableProperty] private Bitmap? _timelinePreviewImage;
    [ObservableProperty] private string _timelinePreviewTime = string.Empty;
    [ObservableProperty] private double _timelinePreviewLeft;

    [ObservableProperty] private bool _isResumePromptVisible;
    [ObservableProperty] private double _resumePromptPosition;
    [ObservableProperty] private string _resumePromptTimeString = string.Empty;

    [ObservableProperty] private bool _isSkipPromptVisible;
    [ObservableProperty] private double _skipPromptOpacity;
    [ObservableProperty] private bool _isSkipPromptHitTestVisible;
    [ObservableProperty] private string _skipPromptTitle = string.Empty;
    [ObservableProperty] private string _skipPromptSubtitle = string.Empty;
    [ObservableProperty] private SkipSegment? _activeSkipSegment;
    [ObservableProperty] private bool _isSkipToastVisible;
    [ObservableProperty] private string _skipToastMessage = string.Empty;

    public PlayerOverlayViewModel()
    {
        _osdTimer.Tick += OnOsdTimerTick;
        _resumePromptTimer.Tick += (s, e) => DismissResumePrompt();
        _skipToastTimer.Tick += (s, e) => DismissSkipToast();
        _skipPromptTimer.Tick += OnSkipPromptTimerTick;
        _skipPromptFadeTimer.Tick += OnSkipPromptFadeTimerTick;
    }

    public void ShowOsd(string message, string detail = "")
    {
        OsdMessage = message;
        OsdDetail = detail;
        IsOsdVisible = true;

        _osdTimer.Stop();
        _osdTimer.Start();
    }

    public void ShowResumePrompt(double positionSeconds, string timeString)
    {
        ResumePromptPosition = positionSeconds;
        ResumePromptTimeString = timeString;
        IsResumePromptVisible = true;

        _resumePromptTimer.Stop();
        _resumePromptTimer.Start();
    }

    public void DismissResumePrompt()
    {
        _resumePromptTimer.Stop();
        IsResumePromptVisible = false;
        ResumePromptPosition = 0;
        ResumePromptTimeString = string.Empty;
    }

    public void ShowSkipPrompt(SkipSegment segment, string title, string subtitle)
    {
        _skipPromptTimer.Stop();
        _skipPromptFadeTimer.Stop();

        ActiveSkipSegment = segment;
        SkipPromptTitle = title;
        SkipPromptSubtitle = subtitle;
        IsSkipPromptVisible = true;
        IsSkipPromptHitTestVisible = true;
        SkipPromptOpacity = 1.0;

        _skipPromptTimer.Start();
    }

    public void DismissSkipPrompt(bool smooth = true)
    {
        _skipPromptTimer.Stop();

        var currentSegment = ActiveSkipSegment;

        if (smooth && IsSkipPromptVisible && SkipPromptOpacity > 0)
        {
            IsSkipPromptHitTestVisible = false;
            SkipPromptOpacity = 0.0;

            if (currentSegment != null)
            {
                SkipPromptDismissed?.Invoke(currentSegment);
            }

            _skipPromptFadeTimer.Stop();
            _skipPromptFadeTimer.Start();
        }
        else
        {
            _skipPromptFadeTimer.Stop();
            SkipPromptOpacity = 0.0;
            IsSkipPromptHitTestVisible = false;
            IsSkipPromptVisible = false;
            ActiveSkipSegment = null;
            SkipPromptTitle = string.Empty;
            SkipPromptSubtitle = string.Empty;

            if (currentSegment != null)
            {
                SkipPromptDismissed?.Invoke(currentSegment);
            }
        }
    }

    public void PauseSkipPromptTimer()
    {
        if (IsSkipPromptVisible && SkipPromptOpacity > 0)
        {
            _skipPromptTimer.Stop();
        }
    }

    public void ResumeSkipPromptTimer()
    {
        if (IsSkipPromptVisible && SkipPromptOpacity > 0)
        {
            _skipPromptTimer.Stop();
            _skipPromptTimer.Start();
        }
    }

    private void OnSkipPromptTimerTick(object? sender, EventArgs e)
    {
        _skipPromptTimer.Stop();
        DismissSkipPrompt(smooth: true);
    }

    private void OnSkipPromptFadeTimerTick(object? sender, EventArgs e)
    {
        _skipPromptFadeTimer.Stop();
        IsSkipPromptVisible = false;
        ActiveSkipSegment = null;
        SkipPromptTitle = string.Empty;
        SkipPromptSubtitle = string.Empty;
    }

    public void ShowSkipToast(string message)
    {
        void Action()
        {
            SkipToastMessage = message;
            IsSkipToastVisible = true;
            _skipToastTimer.Stop();
            _skipToastTimer.Start();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Action();
        else
            Dispatcher.UIThread.Post(Action);
    }

    public void DismissSkipToast()
    {
        _skipToastTimer.Stop();
        IsSkipToastVisible = false;
        SkipToastMessage = string.Empty;
    }
    public void ShowTimelinePreview(double timeSeconds, double left, string? segmentLabel = null)
    {
        TimelinePreviewLeft = Math.Max(0, left);
        var timeStr = PlayerTimelineService.FormatTime(timeSeconds);
        TimelinePreviewTime = string.IsNullOrEmpty(segmentLabel) ? timeStr : $"{timeStr} • {segmentLabel}";
        IsTimelinePreviewVisible = true;
    }

    public void SetTimelinePreviewImage(Bitmap image)
    {
        TimelinePreviewImage?.Dispose();
        TimelinePreviewImage = image;
    }

    public void HideTimelinePreview()
    {
        IsTimelinePreviewVisible = false;
    }

    public void ClearTimelinePreview()
    {
        TimelinePreviewImage?.Dispose();
        TimelinePreviewImage = null;
        TimelinePreviewTime = string.Empty;
        TimelinePreviewLeft = 0;
        IsTimelinePreviewVisible = false;
    }

    private void OnOsdTimerTick(object? sender, EventArgs e)
    {
        _osdTimer.Stop();
        IsOsdVisible = false;
    }

    public void Dispose()
    {
        try
        {
            _osdTimer.Stop();
            _osdTimer.Tick -= OnOsdTimerTick;
        }
        catch (Exception ex) { Log.Debug(ex, "Error stopping OSD timer"); }

        try
        {
            _resumePromptTimer.Stop();
        }
        catch (Exception ex) { Log.Debug(ex, "Error stopping resume prompt timer"); }

        try
        {
            _skipToastTimer.Stop();
        }
        catch (Exception ex) { Log.Debug(ex, "Error stopping skip toast timer"); }

        try
        {
            _skipPromptTimer.Stop();
            _skipPromptTimer.Tick -= OnSkipPromptTimerTick;
        }
        catch (Exception ex) { Log.Debug(ex, "Error stopping skip prompt timer"); }

        try
        {
            _skipPromptFadeTimer.Stop();
            _skipPromptFadeTimer.Tick -= OnSkipPromptFadeTimerTick;
        }
        catch (Exception ex) { Log.Debug(ex, "Error stopping skip prompt fade timer"); }
        finally
        {
            DismissSkipPrompt(smooth: false);
            try { ClearTimelinePreview(); } catch (Exception ex) { Log.Debug(ex, "Error clearing timeline preview"); }
        }
    }
}