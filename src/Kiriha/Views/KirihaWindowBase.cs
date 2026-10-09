using Avalonia.Controls;
using Avalonia.Media;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Views;

public class KirihaWindowBase : Window
{
    protected ISettingsService? SettingsService { get; set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        InstallWin32IconCrashWorkaround(this);

        if (SettingsService != null)
        {
            ApplyUiScale(SettingsService.Current.UI.UiScale);
            ApplyMica();
        }
    }

    /// <summary>
    /// Workaround for an Avalonia UI 12.x bug: <c>Avalonia.Win32.WindowImpl.LoadIcon</c> throws
    /// <see cref="System.NotImplementedException"/> when <c>WM_GETICON</c> receives a <c>wParam</c>
    /// other than ICON_SMALL (0), ICON_BIG (1), or ICON_SMALL2 (2). Some Windows shell
    /// components (e.g. taskbar preview thumbnail close button, display managers, or third-party
    /// shell tweakers like StartAllBack/Windhawk) send <c>WM_GETICON</c> with custom or extended flags.
    /// According to the Win32 specification, unhandled/unsupported icon requests must safely return NULL.
    /// </summary>
    public static void InstallWin32IconCrashWorkaround(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        Win32Properties.AddWndProcHookCallback(window, (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            const uint WM_GETICON = 0x007F;
            if (msg == WM_GETICON)
            {
                int iconType = (int)wParam;
                if (iconType is not (0 or 1 or 2))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            return IntPtr.Zero;
        });
    }

    public void ApplyMica()
    {
        var settings = SettingsService?.Current;
        if (settings is null) return;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur];
            Background = null;
        }
        else
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            ClearValue(BackgroundProperty);
        }
    }

    public void ApplyUiScale(double factor)
    {
        if (this.FindControl<LayoutTransformControl>("ScaleRoot")?.LayoutTransform is ScaleTransform st)
        {
            st.ScaleX = factor;
            st.ScaleY = factor;
        }
    }
}
