using Kiriha.Services.Data.Settings;
using System;
using Avalonia.Controls;
using Avalonia.Media;
using Kiriha.Services.Data;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Views;

public class KirihaWindowBase : Window
{
    protected ISettingsService? SettingsService { get; set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (SettingsService != null)
        {
            ApplyUiScale(SettingsService.Current.UI.UiScale);
            ApplyMica();
        }
    }

    public void ApplyMica()
    {
        var settings = SettingsService?.Current;
        if (settings == null) return;
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
