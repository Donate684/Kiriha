using System;
using Avalonia;
using Avalonia.Controls;

namespace Kiriha.Core.Utils;

public static class WindowExtensions
{
    public static void CenterOnOwnerOrScreenSafe(this Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;

        // 1. Shrink to fit the work area (40 px margin on every side).
        const int marginPx = 40;
        var maxWDip = Math.Max(200, (workArea.Width - marginPx * 2) / scale);
        var maxHDip = Math.Max(150, (workArea.Height - marginPx * 2) / scale);
        if (window.Width > maxWDip) window.Width = maxWDip;
        if (window.Height > maxHDip) window.Height = maxHDip;

        // 2. Compute the actual window footprint in physical pixels.
        var winWPx = (int)Math.Ceiling(window.Width * scale);
        var winHPx = (int)Math.Ceiling(window.Height * scale);

        // 3. Centre on owner when we can, otherwise centre on the screen.
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

        // 4. Clamp inside the work area.
        x = Math.Clamp(x, workArea.X, workArea.X + Math.Max(0, workArea.Width - winWPx));
        y = Math.Clamp(y, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - winHPx));

        window.Position = new PixelPoint(x, y);
    }
}
