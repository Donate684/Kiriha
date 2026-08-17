using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Kiriha.Mpv.UI.ViewModels.Player;

namespace Kiriha.Mpv.UI.Views.Player.Controls;

public partial class PlayerChapterMarkers : UserControl
{
    private PlayerViewModel? _subscribedViewModel;
    private static readonly Geometry s_chapterMarkerGeometry = Geometry.Parse("M0,0 L6,0 L3,3 Z M0,10 L6,10 L3,7 Z");
    private IBrush? _cachedMarkerFill;

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged -= OnChaptersChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as PlayerViewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged += OnChaptersChanged;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        DrawChapterMarkers();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.Duration) || 
            e.PropertyName == nameof(PlayerViewModel.ShowChapterMarkers))
        {
            DrawChapterMarkers();
        }
    }

    private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawChapterMarkers();
    }

    private void OnChapterCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
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

    private void DrawChapterMarkers()
    {
        if (ChapterCanvas == null) return;

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
                var marker = new Avalonia.Controls.Shapes.Path
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

            if (hitArea.Child is Avalonia.Controls.Shapes.Path path)
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
