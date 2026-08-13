using System.Collections.ObjectModel;

namespace Kiriha.ViewModels.History;

public class HistoryGroup
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<HistoryEntryVm> Items { get; } = new();
}
