using Avalonia;

namespace Kiriha.ViewModels.History;

/// <summary>
/// Base class for items displayed in the virtualized history timeline list.
/// </summary>
public abstract class HistoryTimelineItem
{
}

/// <summary>
/// Timeline date group header item (e.g. "Today", "Yesterday", "5 September").
/// </summary>
public sealed class HistoryDateHeaderItem : HistoryTimelineItem
{
    public string Header { get; init; } = string.Empty;
    public bool IsFirst { get; init; }

    public Thickness Margin => IsFirst ? new Thickness(0, 4, 0, 10) : new Thickness(0, 18, 0, 10);
}
