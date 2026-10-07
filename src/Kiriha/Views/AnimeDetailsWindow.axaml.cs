using Avalonia.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Utils;

namespace Kiriha.Views;

public partial class AnimeDetailsWindow : KirihaWindowBase
{
    public AnimeDetailsWindow()
    {
        InitializeComponent();
    }

    public AnimeDetailsWindow(ISettingsService settingsService) : this()
    {
        SettingsService = settingsService;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        this.CenterOnOwnerOrScreenSafe();
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= OnOpened;
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
        DataContext = null;
        base.OnClosed(e);
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }
}
