using Avalonia.Controls;
using Avalonia.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Utils;
using Kiriha.ViewModels.AnimeDetails;

namespace Kiriha.Views;

public partial class AnimeDetailsWindow : KirihaWindowBase
{
    private AnimeDetailsViewModel? _subscribedVm;

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

        if (DataContext is AnimeDetailsViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeDetailsViewModel.Anime))
        {
            var sv = this.FindControl<ScrollViewer>("MainContentScrollViewer");
            if (sv != null)
            {
                sv.Offset = new Avalonia.Vector(0, 0);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsXButton1Pressed &&
            DataContext is AnimeDetailsViewModel { CanGoBack: true } vm)
        {
            vm.GoBackCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= OnOpened;
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
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
