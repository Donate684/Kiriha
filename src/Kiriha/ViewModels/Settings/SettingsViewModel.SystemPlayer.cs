using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel
{
    public partial class PlayerSelectionItem : ObservableObject
    {
        public string Name { get; }
        public Kiriha.Core.Domain.Models.PlayerType Type { get; }

        [ObservableProperty]
        private bool _isEnabled;

        public PlayerSelectionItem(string name, Kiriha.Core.Domain.Models.PlayerType type, bool isEnabled)
        {
            Name = name;
            Type = type;
            _isEnabled = isEnabled;
        }
    }
}


