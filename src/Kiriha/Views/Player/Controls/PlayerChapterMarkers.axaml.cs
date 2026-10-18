using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player;
using Path = Avalonia.Controls.Shapes.Path;

namespace Kiriha.Views.Player.Controls;

public partial class PlayerChapterMarkers : UserControl
{
    private PlayerViewModel? _subscribedViewModel;
    private static readonly Geometry s_chapterMarkerGeometry = Geometry.Parse("M0,0 L6,0 L3,3 Z M0,10 L6,10 L3,7 Z");
    private IBrush? _cachedMarkerFill;

    // Skip segment color brushes (accented modern palette for OP, ED, Mixed OP/ED, Recap, Preview)
    private static readonly IBrush s_introBrush = new SolidColorBrush(Color.FromArgb(230, 255, 150, 0));       // Vibrant Orange (OP)
    private static readonly IBrush s_outroBrush = new SolidColorBrush(Color.FromArgb(230, 230, 80, 96));       // Coral Red (ED)
    private static readonly IBrush s_mixedIntroBrush = new SolidColorBrush(Color.FromArgb(230, 255, 183, 77)); // Amber/Gold (Mixed OP)
    private static readonly IBrush s_mixedOutroBrush = new SolidColorBrush(Color.FromArgb(230, 186, 104, 200));// Soft Violet (Mixed ED)
    private static readonly IBrush s_recapBrush = new SolidColorBrush(Color.FromArgb(230, 47, 128, 255));      // Tech Blue (Recap)
    private static readonly IBrush s_previewBrush = new SolidColorBrush(Color.FromArgb(230, 0, 212, 199));     // Cyan (Preview)

    private static IBrush GetSegmentBrush(SkipSegmentType type) => type switch
    {
        SkipSegmentType.Intro => s_introBrush,
        SkipSegmentType.Outro => s_outroBrush,
        SkipSegmentType.MixedIntro => s_mixedIntroBrush,
        SkipSegmentType.MixedOutro => s_mixedOutroBrush,
        SkipSegmentType.Recap => s_recapBrush,
        _ => s_previewBrush
    };

    private IBrush GetMarkerFill()
    {
        if (_cachedMarkerFill != null) return _cachedMarkerFill;
        
        if (Application.Current!.TryGetResource("SystemAccentColor", out var res) && res is Color accentColor)
        {
            _cachedMarkerFill = new SolidColorBrush(accentColor);
        }
        else
        {
            _cachedMarkerFill = Brushes.White;
        }
        
        return _cachedMarkerFill;
    }

    public PlayerChapterMarkers()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_subscribedViewModel == null && DataContext is PlayerViewModel vm)
        {
            _subscribedViewModel = vm;
            _subscribedViewModel.Chapters.CollectionChanged += OnChaptersChanged;
            _subscribedViewModel.SkipSegments.CollectionChanged += OnSkipSegmentsChanged;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            DrawAll();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged -= OnChaptersChanged;
            _subscribedViewModel.SkipSegments.CollectionChanged -= OnSkipSegmentsChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged -= OnChaptersChanged;
            _subscribedViewModel.SkipSegments.CollectionChanged -= OnSkipSegmentsChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as PlayerViewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged += OnChaptersChanged;
            _subscribedViewModel.SkipSegments.CollectionChanged += OnSkipSegmentsChanged;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        DrawAll();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.Duration) || 
            e.PropertyName == nameof(PlayerViewModel.ShowChapterMarkers))
        {
            DrawAll();
        }
    }

    private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawChapterMarkers();
    }

    private void OnSkipSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawSkipSegments();
    }

    private void OnChapterCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        DrawAll();
    }

    private void DrawAll()
    {
        DrawSkipSegments();
        DrawChapterMarkers();
    }

    private void OnChapterMarkerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.Tag is double chapterTime && DataContext is PlayerViewModel playerVm)
        {
            playerVm.SeekTo(chapterTime);
            e.Handled = true;
        }
    }

    private void DrawSkipSegments()
    {
        if (SegmentCanvas is null) return;

        if (DataContext is not PlayerViewModel vm || !vm.ShowChapterMarkers || vm.Duration <= 0 || vm.SkipSegments.Count == 0)
        {
            foreach (var child in SegmentCanvas.Children)
            {
                child.IsVisible = false;
            }
            return;
        }

        double trackWidth = SegmentCanvas.Bounds.Width;
        double trackHeight = SegmentCanvas.Bounds.Height;
        if (trackWidth <= 0 || trackHeight <= 0) return;

        double duration = vm.Duration;
        double centerY = trackHeight / 2.0;
        double barHeight = 4.5;
        double top = centerY - (barHeight / 2.0);

        int childIndex = 0;

        foreach (var segment in vm.SkipSegments)
        {
            if (segment.EndSeconds <= segment.StartSeconds || segment.StartSeconds >= duration)
                continue;

            double startRatio = Math.Clamp(segment.StartSeconds / duration, 0.0, 1.0);
            double endRatio = Math.Clamp(segment.EndSeconds / duration, 0.0, 1.0);

            double startX = startRatio * trackWidth;
            double endX = endRatio * trackWidth;
            double width = Math.Max(3.0, endX - startX);

            Border bar;
            if (childIndex < SegmentCanvas.Children.Count)
            {
                bar = (Border)SegmentCanvas.Children[childIndex];
                bar.IsVisible = true;
            }
            else
            {
                bar = new Border
                {
                    Height = barHeight,
                    CornerRadius = new CornerRadius(2.25),
                    IsHitTestVisible = false
                };
                SegmentCanvas.Children.Add(bar);
            }

            bar.Background = GetSegmentBrush(segment.Type);
            bar.Width = width;

            Canvas.SetLeft(bar, startX);
            Canvas.SetTop(bar, top);

            childIndex++;
        }

        for (int i = childIndex; i < SegmentCanvas.Children.Count; i++)
        {
            SegmentCanvas.Children[i].IsVisible = false;
        }
    }

    private void DrawChapterMarkers()
    {
        if (ChapterCanvas is null) return;

        if (DataContext is not PlayerViewModel vm || !vm.ShowChapterMarkers || vm.Duration <= 0 || vm.Chapters.Count == 0)
        {
            foreach (var child in ChapterCanvas.Children)
            {
                child.IsVisible = false;
            }
            return;
        }

        double trackWidth = ChapterCanvas.Bounds.Width;
        double trackHeight = ChapterCanvas.Bounds.Height;
        if (trackWidth <= 0 || trackHeight <= 0) return;

        IBrush markerFill = GetMarkerFill();

        int childIndex = 0;
        double duration = vm.Duration;
        double centerY = trackHeight / 2.0;

        foreach (var chapter in vm.Chapters)
        {
            if (chapter.Time <= 0) continue;

            Border hitArea;
            if (childIndex < ChapterCanvas.Children.Count)
            {
                hitArea = (Border)ChapterCanvas.Children[childIndex];
                hitArea.IsVisible = true;
            }
            else
            {
                var marker = new Path
                {
                    Data = s_chapterMarkerGeometry,
                    Opacity = 0.90,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                hitArea = new Border
                {
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Width = 32,
                    Height = 32,
                    Child = marker
                };

                hitArea.PointerPressed += OnChapterMarkerPointerPressed;
                ChapterCanvas.Children.Add(hitArea);
            }

            hitArea.Tag = chapter.Time;

            if (hitArea.Child is Path path)
            {
                path.Fill = markerFill;
            }

            double ratio = Math.Clamp(chapter.Time / duration, 0.0, 1.0);
            double x = ratio * trackWidth;

            Canvas.SetLeft(hitArea, x - 16.0);
            Canvas.SetTop(hitArea, centerY - 16.0);

            childIndex++;
        }

        for (int i = childIndex; i < ChapterCanvas.Children.Count; i++)
        {
            ChapterCanvas.Children[i].IsVisible = false;
        }
    }
}
