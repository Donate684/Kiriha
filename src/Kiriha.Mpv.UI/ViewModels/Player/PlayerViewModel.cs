using System.Collections.ObjectModel;
using System.Collections.Frozen;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private static readonly FrozenSet<string> MediaExtensions = FrozenSet.ToFrozenSet(
        [
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg"
        ], StringComparer.OrdinalIgnoreCase);

    private readonly IPlayerMediaMetadataResolver? _metadataResolver;
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizer _localizer;
    private readonly PlayerPlaybackController _playback = new();
    private readonly PlayerStatePublisher _statePublisher;
    private readonly PlayerTimelineService _timeline = new();
    private readonly PlayerSettingsApplier _settingsApplier;
    private readonly PlayerTimelinePreviewController _timelinePreview;
    private DispatcherTimer? _timer;
    private bool _isApplyingSettings;
    private bool _mpvRuntimeDiagnosticsVisible;
    private double _subtitleDelay;

    public PlayerOverlayViewModel Overlay { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> AudioTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> SubtitleTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ChapterInfo> Chapters { get; } = new();

    [ObservableProperty] private string _videoUrl = string.Empty;
    [ObservableProperty] private string _originalTitle = string.Empty;
    [ObservableProperty] private string _animeTitle = string.Empty;
    [ObservableProperty] private string _animeTitleRu = string.Empty;
    [ObservableProperty] private string _animeTitleEn = string.Empty;
    [ObservableProperty] private string _animeTitleRomaji = string.Empty;
    [ObservableProperty] private string _episodeTitle = string.Empty;
    [ObservableProperty] private string _rawEpisodeText = string.Empty;


    private int? _animeId;
    public int? AnimeId => _animeId;
    private bool _isInitializing;
    private string? _previousVideoUrlForMetadata;

    private readonly ITorrServerService? _torrServer;
    private string? _pendingSubtitleUrl;

    public string? PendingSubtitleUrl => _pendingSubtitleUrl;

    public PlayerViewModel(
        string videoUrl,
        PlayerMediaMetadata? metadata,
        IPlayerMediaMetadataResolver? metadataResolver,
        ISettingsService? settingsService,
        ILocalizer localizer,
        ITorrServerService? torrServer = null,
        string? subtitleUrl = null)
    {
        _isInitializing = true;
        _metadataResolver = metadataResolver;
        _settingsService = settingsService;
        _localizer = localizer;
        _torrServer = torrServer;
        _pendingSubtitleUrl = subtitleUrl;
        InitializeOptions();
        _statePublisher = new PlayerStatePublisher(CreatePlayerState);
        _statePublisher.MetadataReceived += OnExternalMetadataReceived;
        _settingsApplier = new PlayerSettingsApplier(_playback);
        _timelinePreview = new PlayerTimelinePreviewController(Overlay);
        Overlay.SkipPromptDismissed += OnOverlaySkipPromptDismissed;
        _playback.BufferingChanged += OnPlayerBufferingChanged;
        ApplyPlayerSettings();
        var initialMetadata = metadata ?? metadataResolver?.Resolve(videoUrl) ?? PlayerMediaMetadata.FromVideoPath(videoUrl);
        ApplyMetadata(initialMetadata);

        _previousVideoUrlForMetadata = videoUrl;
        VideoUrl = videoUrl; // Sets VideoUrl and triggers OnVideoUrlChanged if needed, but since it's constructor, we already set the fields above.
        InitializeTorrentStream(videoUrl, initialMetadata);
        _isInitializing = false;
    }

    private void OnExternalMetadataReceived(PlayerMediaMetadata metadata)
    {
        bool isMatch = MatchesTorrentHash(metadata.TorrentHash) ||
                       (_animeId.HasValue && metadata.AnimeId.HasValue && _animeId.Value == metadata.AnimeId.Value) ||
                       MatchesOriginalTitle(metadata.OriginalTitle) ||
                       MatchesOriginalTitle(metadata.TitleRomaji) ||
                       MatchesOriginalTitle(metadata.TitleEn) ||
                       MatchesOriginalTitle(metadata.TitleRu);

        if (isMatch)
        {
            Dispatcher.UIThread.Post(() => ApplyExternalMetadata(metadata));
        }
    }



    private readonly List<SkipSegment> _skipSegments = new();
    public ObservableCollection<SkipSegment> SkipSegments { get; } = new();
    private readonly HashSet<SkipSegment> _skippedSegments = new();

    public void RefreshSkipSegments()
    {
        _skippedSegments.Clear();
        var chapterSegments = SkipMarkerResolver.ResolveChapters(Chapters.ToList(), Duration);
        _skipSegments.Clear();
        _skipSegments.AddRange(chapterSegments);
        UpdateSkipSegmentsCollection();
        CheckSkipSegments(CurrentTime, Duration);
    }

    
    private bool IsSegmentTypeEnabled(SkipSegmentType type) => type switch
    {
        SkipSegmentType.Intro => SkipOp,
        SkipSegmentType.Outro => SkipEd,
        SkipSegmentType.MixedIntro => SkipMixedOp,
        SkipSegmentType.MixedOutro => SkipMixedEd,
        SkipSegmentType.Recap => SkipRecap,
        _ => true
    };

    private void UpdateSkipSegmentsCollection()
    {
        SkipSegments.Clear();
        foreach (var seg in _skipSegments)
        {
            SkipSegments.Add(seg);
        }
    }
    private void OnOverlaySkipPromptDismissed(SkipSegment segment)
    {
        _skippedSegments.Add(segment);
    }

    private void CheckSkipSegments(double currentTime, double duration)
    {
        if (SkipOpeningEnding == SkipOpeningEndingMode.Disabled)
        {
            if (Overlay.IsSkipPromptVisible)
                Overlay.DismissSkipPrompt(smooth: false);
            return;
        }

        _skippedSegments.RemoveWhere(s => currentTime < s.StartSeconds - 1.0);

        var active = _skipSegments.FirstOrDefault(s =>
            currentTime >= s.StartSeconds &&
            currentTime < (s.EndSeconds - 0.5) &&
            IsSegmentTypeEnabled(s.Type) &&
            !_skippedSegments.Contains(s));

        if (active != null)
        {
            if (SkipOpeningEnding == SkipOpeningEndingMode.Auto)
            {
                if (IsPlaying)
                {
                    _skippedSegments.Add(active);
                    var target = SkipMarkerResolver.GetSeekTarget(active, duration);
                    _playback.Seek(target);
                    NotifySegmentSkipped(active);
                }
            }
            else if (SkipOpeningEnding == SkipOpeningEndingMode.Prompt)
            {
                if (Overlay.IsResumePromptVisible)
                    return;

                if (Overlay.ActiveSkipSegment != active || !Overlay.IsSkipPromptVisible)
                {
                    var promptTitle = active.Type switch
                    {
                        SkipSegmentType.Intro => _localizer.GetLoc("player.skip.op_prompt"),
                        SkipSegmentType.Outro => _localizer.GetLoc("player.skip.ed_prompt"),
                        SkipSegmentType.MixedIntro => _localizer.GetLoc("player.skip.mixed_op_prompt"),
                        SkipSegmentType.MixedOutro => _localizer.GetLoc("player.skip.mixed_ed_prompt"),
                        SkipSegmentType.Recap => _localizer.GetLoc("player.skip.recap_prompt"),
                        _ => _localizer.GetLoc("player.skip.preview_prompt")
                    };
                    var targetTime = PlayerTimelineService.FormatTime(active.EndSeconds);
                    var subtitle = $"{_localizer.GetLoc("player.skip.until")} {targetTime}";
                    Overlay.ShowSkipPrompt(active, promptTitle, subtitle);
                }
            }
        }
        else
        {
            if (Overlay.IsSkipPromptVisible && Overlay.ActiveSkipSegment != null)
            {
                if (currentTime >= Overlay.ActiveSkipSegment.EndSeconds || currentTime < Overlay.ActiveSkipSegment.StartSeconds)
                {
                    Overlay.DismissSkipPrompt(smooth: false);
                }
            }
        }
    }
}
