using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models.Entities;
using System;
using Kiriha.Core;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using Kiriha.Services.AppLifecycle;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
namespace Kiriha;
public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    private AppStartupCoordinator? _startupCoordinator;
    private PlayerModeCoordinator? _playerModeCoordinator;
    private ShutdownCoordinator? _shutdownCoordinator;
    private TrayService? _trayService;
    public override void OnFrameworkInitializationCompleted()
    {
        AnimeEntityPresentation.GetLoc = (k, args) => Kiriha.Core.UIUtils.GetLoc(k, args);
        AppStartupCoordinator.InstallUnhandledExceptionHandler();
        var args = Environment.GetCommandLineArgs();
        var isPlayerMode = PlayerModeCoordinator.IsPlayerMode(args);
        ServiceProvider = AppStartupCoordinator.BuildServiceProvider(isPlayerMode);
        _shutdownCoordinator = new ShutdownCoordinator(ServiceProvider, ServiceProvider.GetServices<Kiriha.Services.AppLifecycle.Shutdown.IShutdownHandler>());
        _trayService = new TrayService(this, ServiceProvider, _shutdownCoordinator);
        if (isPlayerMode)
        {
            _playerModeCoordinator = new PlayerModeCoordinator(this, ServiceProvider, _trayService);
            _playerModeCoordinator.Initialize(args);
            base.OnFrameworkInitializationCompleted();
            _trayService.DisableTrayIcons();
            return;
        }
        _startupCoordinator = new AppStartupCoordinator(this, ServiceProvider, _trayService, _shutdownCoordinator);
        _startupCoordinator.Initialize(args);
        if (Current?.PlatformSettings != null)
        {
            Current.PlatformSettings.ColorValuesChanged += (sender, e) =>
            {
                if (ServiceProvider != null)
                {
                    var settings = ServiceProvider.GetRequiredService<Kiriha.Services.Data.Settings.SettingsService>();
                    ApplyCustomAccentColor(settings.Current.UI.CustomAccentColor);
                }
            };
        }
        if (ServiceProvider != null)
        {
            var settings = ServiceProvider.GetRequiredService<Kiriha.Services.Data.Settings.SettingsService>();
            ApplyCustomAccentColor(settings.Current.UI.CustomAccentColor);
        }
        base.OnFrameworkInitializationCompleted();
    }
    public override void Initialize()
    {
        if (PlayerModeCoordinator.IsPlayerMode(Environment.GetCommandLineArgs()))
        {
            Styles.Add(new FluentTheme());
            Styles.Add(new MaterialIconStyles(null));
            return;
        }
        AvaloniaXamlLoader.Load(this);
    }
    public void UpdateTrayMenu() => _trayService?.UpdateTrayMenu();
    private void TrayRestore_Click(object? sender, EventArgs e) => _trayService?.RestoreMainWindow();
    private void TrayExit_Click(object? sender, EventArgs e) => _trayService?.Exit();
    private static Avalonia.Controls.ResourceDictionary? _customAccentDictionary;
    public static void ApplyCustomAccentColor(string? hexCode)
    {
        if (Current == null) return;
        Avalonia.Media.Color baseColor;
        if (string.IsNullOrWhiteSpace(hexCode) || !Avalonia.Media.Color.TryParse(hexCode, out var c))
        {
            var sysColor = Current.PlatformSettings?.GetColorValues().AccentColor1;
            
            // Workaround for Avalonia bug where GetColorValues might return White initially
            if (sysColor.HasValue && sysColor.Value.A > 0 && !(sysColor.Value.R == 255 && sysColor.Value.G == 255 && sysColor.Value.B == 255))
            {
                baseColor = Avalonia.Media.Color.FromArgb(255, sysColor.Value.R, sysColor.Value.G, sysColor.Value.B);
            }
            else
            {
                baseColor = GetWindowsAccentColor();
            }
        }
        else
        {
            baseColor = c;
        }
        var newDict = new Avalonia.Controls.ResourceDictionary();
        newDict["SystemAccentColor"] = baseColor;
        newDict["SystemAccentColorDark1"] = baseColor;
        newDict["SystemAccentColorDark2"] = baseColor;
        newDict["SystemAccentColorDark3"] = baseColor;
        newDict["SystemAccentColorLight1"] = baseColor;
        newDict["SystemAccentColorLight2"] = baseColor;
        newDict["SystemAccentColorLight3"] = baseColor;
        if (_customAccentDictionary != null)
        {
            Current.Resources.MergedDictionaries.Remove(_customAccentDictionary);
        }
        
        _customAccentDictionary = newDict;
        Current.Resources.MergedDictionaries.Add(_customAccentDictionary);
    }
    private static Avalonia.Media.Color GetWindowsAccentColor()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (key?.GetValue("ColorizationColor") is int color)
                {
                    var r = (byte)((color >> 16) & 0xFF);
                    var g = (byte)((color >> 8) & 0xFF);
                    var b = (byte)(color & 0xFF);
                    return Avalonia.Media.Color.FromArgb(255, r, g, b);
                }
            }
        }
        catch { }
        return Avalonia.Media.Color.Parse("#FF0078D7");
    }
}
