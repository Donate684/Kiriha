using Avalonia.Controls;

namespace Kiriha.Core.Infrastructure;

public static class WindowPositioningHelper
{
    public static void CenterOnOwnerOrScreen(Window window, int marginPx = 40)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;

        var maxWDip = System.Math.Max(200, (workArea.Width - marginPx * 2) / scale);
        var maxHDip = System.Math.Max(150, (workArea.Height - marginPx * 2) / scale);
        if (window.Width > maxWDip) window.Width = maxWDip;
        if (window.Height > maxHDip) window.Height = maxHDip;

        var winWPx = (int)System.Math.Ceiling(window.Width * scale);
        var winHPx = (int)System.Math.Ceiling(window.Height * scale);

        int x, y;
        if (window.Owner is Window owner && owner.IsVisible)
        {
            var ownerWPx = (int)(owner.Bounds.Width * scale);
            var ownerHPx = (int)(owner.Bounds.Height * scale);
            x = owner.Position.X + (ownerWPx - winWPx) / 2;
            y = owner.Position.Y + (ownerHPx - winHPx) / 2;
        }
        else
        {
            x = workArea.X + (workArea.Width - winWPx) / 2;
            y = workArea.Y + (workArea.Height - winHPx) / 2;
        }

        x = System.Math.Clamp(x, workArea.X, workArea.X + System.Math.Max(0, workArea.Width - winWPx));
        y = System.Math.Clamp(y, workArea.Y, workArea.Y + System.Math.Max(0, workArea.Height - winHPx));

        window.Position = new Avalonia.PixelPoint(x, y);
    }
}
