using Kiriha.Services.Data.Settings;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Services.Data;

namespace Kiriha.Views;

public partial class AboutWindow : KirihaWindowBase
{
    public IReadOnlyList<CreditEntry> DataSources => AboutCredits.DataSources;
    public IReadOnlyList<CreditEntry> Inspirations => AboutCredits.Inspirations;
    public IReadOnlyList<CreditEntry> Libraries => AboutCredits.Libraries;

    public AboutWindow()
    {
        DataContext = this;
        InitializeComponent();
    }

    public AboutWindow(SettingsService settingsService) : this()
    {
        SettingsService = settingsService;
        ApplyMica();
        VersionLabel.Text = $"v{AppInfo.Version}".ToUpperInvariant();
        Opened += OnOpened;
    }

    public new void ApplyMica()
    {
        var settings = SettingsService?.Current;
        if (settings == null) return;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.Mica, Avalonia.Controls.WindowTransparencyLevel.AcrylicBlur };
            Background = null;
        }
        else
        {
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None };
            ClearValue(BackgroundProperty);
        }
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        WindowPositioningHelper.CenterOnOwnerOrScreen(this);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        var p = e.GetCurrentPoint(this);
        if (!p.Properties.IsLeftButtonPressed) return;
        if (p.Position.Y > 34) return;
        BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>
    /// Generic row click — the URL is passed via the Button.Tag binding, so
    /// the same handler serves both Data Sources and Libraries lists. Rows
    /// without a URL get an empty Tag and are no-ops (the OpenInNew icon is
    /// hidden for them via HasUrl binding).
    /// </summary>
    private void OnEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrEmpty(url))
            UIUtils.OpenUrl(url);
    }
}
