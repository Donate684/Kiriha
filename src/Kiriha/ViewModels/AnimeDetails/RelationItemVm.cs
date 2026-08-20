using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class RelationItemVm : ObservableObject
{
    public AnimeRelation Relation { get; }

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private string? _displayTargetType;

    public RelationItemVm(AnimeRelation relation)
    {
        Relation = relation;
        DisplayTargetType = relation.TargetType;
    }
}
