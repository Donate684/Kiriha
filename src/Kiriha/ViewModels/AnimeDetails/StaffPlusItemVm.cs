using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class StaffPlusItemVm : ObservableObject
{
    public Kiriha.Core.Domain.Models.Entities.AnimeStaff Staff { get; }

    [ObservableProperty]
    private string _role = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<StaffWorkVm> BestWorks { get; } = new();

    public StaffPlusItemVm(Kiriha.Core.Domain.Models.Entities.AnimeStaff staff)
    {
        Staff = staff;
    }
}
