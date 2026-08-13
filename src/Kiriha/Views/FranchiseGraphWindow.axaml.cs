using Kiriha.ViewModels.Dialogs;
using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Kiriha.ViewModels;
using Avalonia.Media;

namespace Kiriha.Views;

public partial class FranchiseGraphWindow : KirihaWindowBase
{
    private bool _isPanning;
    private Point _lastPanPoint;

    private ScaleTransform GraphScale 
    {
        get 
        {
            var grid = (Grid)GraphContainer.Child!;
            var group = (TransformGroup)grid.RenderTransform!;
            return (ScaleTransform)group.Children[0];
        }
    }

    private TranslateTransform GraphTranslate
    {
        get 
        {
            var grid = (Grid)GraphContainer.Child!;
            var group = (TransformGroup)grid.RenderTransform!;
            return (TranslateTransform)group.Children[1];
        }
    }

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

        if (DataContext is FranchiseGraphViewModel vm)
        {
            await vm.LoadGraphAsync();
            CenterGraph();
        }
    }

    private void CenterGraph()
    {
        if (DataContext is FranchiseGraphViewModel vm && vm.Layout != null && GraphTranslate != null)
        {
            double windowWidth = this.Bounds.Width;
            double windowHeight = this.Bounds.Height;
            
            double graphWidth = vm.Layout.Width;
            double graphHeight = vm.Layout.Height;

            GraphTranslate.X = (windowWidth - graphWidth) / 2;
            GraphTranslate.Y = (windowHeight - graphHeight) / 2;
            
            GraphScale.ScaleX = 1.0;
            GraphScale.ScaleY = 1.0;
        }
    }

    private void OnGraphPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Allow pan if left button is pressed, and we didn't click on a button (Node)
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            // We don't mark as handled here to let buttons work if clicked
        }
    }

    private void OnGraphPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
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
        var zoomDelta = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var newScale = GraphScale.ScaleX * zoomDelta;

        if (newScale < 0.1) newScale = 0.1;
        if (newScale > 4.0) newScale = 4.0;

        zoomDelta = newScale / GraphScale.ScaleX;

        var mousePos = e.GetPosition(GraphContainer);

        GraphTranslate.X = (GraphTranslate.X - mousePos.X) * zoomDelta + mousePos.X;
        GraphTranslate.Y = (GraphTranslate.Y - mousePos.Y) * zoomDelta + mousePos.Y;

        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;

        e.Handled = true;
    }
}
