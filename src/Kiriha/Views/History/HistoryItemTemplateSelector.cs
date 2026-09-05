using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kiriha.ViewModels.History;

namespace Kiriha.Views.History;

/// <summary>
/// Data template selector for the virtualized history timeline items repeater.
/// Implements <see cref="IRecyclingDataTemplate"/> to enable high-performance element recycling.
/// </summary>
public class HistoryItemTemplateSelector : IRecyclingDataTemplate
{
    public IDataTemplate? HeaderTemplate { get; set; }
    public IDataTemplate? EntryTemplate { get; set; }

    public Control? Build(object? param) => Build(param, null);

    public Control? Build(object? data, Control? existing)
    {
        return data switch
        {
            HistoryDateHeaderItem => (existing?.DataContext is HistoryDateHeaderItem)
                ? ((HeaderTemplate as IRecyclingDataTemplate)?.Build(data, existing) ?? existing)
                : HeaderTemplate?.Build(data),

            HistoryEntryVm => (existing?.DataContext is HistoryEntryVm)
                ? ((EntryTemplate as IRecyclingDataTemplate)?.Build(data, existing) ?? existing)
                : EntryTemplate?.Build(data),

            _ => null
        };
    }

    public bool Match(object? data) => data is HistoryTimelineItem;
}
