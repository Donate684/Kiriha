using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kiriha.ViewModels.Dialogs;

namespace Kiriha.Views;

public partial class FranchiseGraphWindow : KirihaWindowBase
{
    private bool _isPanning;
    private Point _lastPanPoint;

    private ScaleTransform? GraphScale =>
        (GraphContainer?.Child as Grid)?.RenderTransform is TransformGroup group && group.Children.Count > 0
            ? group.Children[0] as ScaleTransform
            : null;

    private TranslateTransform? GraphTranslate =>
        (GraphContainer?.Child as Grid)?.RenderTransform is TransformGroup group && group.Children.Count > 1
            ? group.Children[1] as TranslateTransform
            : null;

    public FranchiseGraphWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            if (DataContext is FranchiseGraphViewModel vm)
            {
                vm.RequestCenterGraph += CenterGraph;
                vm.RequestZoomDelta += OnRequestZoomDelta;
                vm.RequestResetZoom += OnRequestResetZoom;

                await vm.LoadGraphAsync();
                CenterGraph();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "FranchiseGraphWindow.OnOpened failed");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is FranchiseGraphViewModel vm)
        {
            vm.RequestCenterGraph -= CenterGraph;
            vm.RequestZoomDelta -= OnRequestZoomDelta;
            vm.RequestResetZoom -= OnRequestResetZoom;
        }

        base.OnClosed(e);
    }

    private void CenterGraph()
    {
        if (DataContext is FranchiseGraphViewModel vm && vm.Layout != null && GraphTranslate != null && GraphScale != null)
        {
            double windowWidth = GraphContainer.Bounds.Width > 0 ? GraphContainer.Bounds.Width : this.Bounds.Width;
            double windowHeight = GraphContainer.Bounds.Height > 0 ? GraphContainer.Bounds.Height : this.Bounds.Height - 44;

            double graphWidth = vm.Layout.Width;
            double graphHeight = vm.Layout.Height;

            if (graphWidth <= 0 || graphHeight <= 0) return;

            double padding = 80;
            double scaleX = (windowWidth - padding) / graphWidth;
            double scaleY = (windowHeight - padding) / graphHeight;
            double fitScale = Math.Min(1.0, Math.Min(scaleX, scaleY));
            if (fitScale < 0.25) fitScale = 0.25;
            if (fitScale > 1.25) fitScale = 1.0;

            GraphScale.ScaleX = fitScale;
            GraphScale.ScaleY = fitScale;

            GraphTranslate.X = (windowWidth - graphWidth * fitScale) / 2;
            GraphTranslate.Y = Math.Max(24, (windowHeight - graphHeight * fitScale) / 2);

            vm.UpdateZoomDisplay(fitScale);
        }
    }

    private void OnRequestZoomDelta(double delta)
    {
        if (GraphScale == null || GraphTranslate == null || DataContext is not FranchiseGraphViewModel vm) return;

        double currentScale = GraphScale.ScaleX;
        double newScale = Math.Clamp(currentScale * delta, 0.2, 3.0);
        double actualDelta = newScale / currentScale;

        double centerX = GraphContainer.Bounds.Width > 0 ? GraphContainer.Bounds.Width / 2 : this.Bounds.Width / 2;
        double centerY = GraphContainer.Bounds.Height > 0 ? GraphContainer.Bounds.Height / 2 : this.Bounds.Height / 2;

        GraphTranslate.X = (GraphTranslate.X - centerX) * actualDelta + centerX;
        GraphTranslate.Y = (GraphTranslate.Y - centerY) * actualDelta + centerY;

        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;

        vm.UpdateZoomDisplay(newScale);
    }

    private void OnRequestResetZoom()
    {
        if (GraphScale == null || GraphTranslate == null || DataContext is not FranchiseGraphViewModel vm) return;

        double currentScale = GraphScale.ScaleX;
        double newScale = 1.0;
        double actualDelta = newScale / currentScale;

        double centerX = GraphContainer.Bounds.Width > 0 ? GraphContainer.Bounds.Width / 2 : this.Bounds.Width / 2;
        double centerY = GraphContainer.Bounds.Height > 0 ? GraphContainer.Bounds.Height / 2 : this.Bounds.Height / 2;

        GraphTranslate.X = (GraphTranslate.X - centerX) * actualDelta + centerX;
        GraphTranslate.Y = (GraphTranslate.Y - centerY) * actualDelta + centerY;

        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;

        vm.UpdateZoomDisplay(newScale);
    }

    private void OnGraphPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
        }
    }

    private void OnGraphPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning && GraphTranslate != null)
        {
            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _lastPanPoint;

            GraphTranslate.X += delta.X;
            GraphTranslate.Y += delta.Y;

            _lastPanPoint = currentPoint;
            e.Handled = true;
        }
    }

    private void OnGraphPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
    }

    private void OnGraphPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (GraphScale == null || GraphTranslate == null) return;

        var zoomFactor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(GraphScale.ScaleX * zoomFactor, 0.18, 3.5);
        var actualDelta = newScale / GraphScale.ScaleX;

        var mousePos = e.GetPosition(GraphContainer);

        GraphTranslate.X = (GraphTranslate.X - mousePos.X) * actualDelta + mousePos.X;
        GraphTranslate.Y = (GraphTranslate.Y - mousePos.Y) * actualDelta + mousePos.Y;

        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;

        if (DataContext is FranchiseGraphViewModel vm)
        {
            vm.UpdateZoomDisplay(newScale);
        }

        e.Handled = true;
    }
}
