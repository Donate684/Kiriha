using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kiriha.ViewModels.History;

namespace Kiriha.Views.History;

/// <summary>
/// Recycling element factory for the virtualized history timeline items repeater.
/// Inherits from <see cref="RecyclingElementFactory"/> to maintain separate recycle pools
/// for header items and entry cards, preventing cross-type template recycling corruption.
/// </summary>
public class HistoryItemTemplateSelector : RecyclingElementFactory
{
    public IDataTemplate? HeaderTemplate
    {
        get => Templates.TryGetValue("Header", out var t) ? t : null;
        set { if (value != null) Templates["Header"] = value; }
    }

    public IDataTemplate? EntryTemplate
    {
        get => Templates.TryGetValue("Entry", out var t) ? t : null;
        set { if (value != null) Templates["Entry"] = value; }
    }

    protected override string OnSelectTemplateKeyCore(object? dataContext, Control? owner)
    {
        return dataContext switch
        {
            HistoryDateHeaderItem => "Header",
            HistoryEntryVm => "Entry",
            _ => base.OnSelectTemplateKeyCore(dataContext, owner)
        };
    }

    protected override Control GetElementCore(ElementFactoryGetArgs args)
    {
        var element = base.GetElementCore(args);
        element.DataContext = args.Data;
        return element;
    }
}
