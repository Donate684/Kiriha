using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class StaffPlusItemVm : ObservableObject
{
    public AnimeStaff Staff { get; }

    [ObservableProperty]
    private string _role = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<StaffWorkVm> BestWorks { get; } = new();

    public StaffPlusItemVm(AnimeStaff staff)
    {
        Staff = staff;
    }
}
