using Avalonia.Controls;
using Kiriha.ViewModels.Analytics;

namespace Kiriha.Views.Analytics;

public partial class MalDeepParserWindow : KirihaWindowBase
{
    public MalDeepParserWindow()
    {
        InitializeComponent();
    }

    public MalDeepParserWindow(MalDeepParserViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MalDeepParserViewModel vm && vm.IsRunning)
        {
            vm.Stop();
        }
        Close();
    }
}
