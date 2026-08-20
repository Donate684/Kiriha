using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel
{
    public partial class PlayerSelectionItem : ObservableObject
    {
        public string Name { get; }
        public PlayerType Type { get; }

        [ObservableProperty]
        private bool _isEnabled;

        public PlayerSelectionItem(string name, PlayerType type, bool isEnabled)
        {
            Name = name;
            Type = type;
            _isEnabled = isEnabled;
        }
    }
}


