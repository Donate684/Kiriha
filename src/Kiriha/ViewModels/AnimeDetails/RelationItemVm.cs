using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class RelationItemVm : ObservableObject
{
    public Kiriha.Core.Abstractions.Models.Entities.AnimeRelation Relation { get; }

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private string? _displayTargetType;

    public RelationItemVm(Kiriha.Core.Abstractions.Models.Entities.AnimeRelation relation)
    {
        Relation = relation;
        DisplayTargetType = relation.TargetType;
    }
}
