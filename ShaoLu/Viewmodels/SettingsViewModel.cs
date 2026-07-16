using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;

namespace ShaoLu.Viewmodels
{
    public class SettingsViewModel : ObservableObject
    {

        private Settings _selectedSetItem;
        public Settings SelectedSetItem { get => _selectedSetItem; set => SetProperty(ref _selectedSetItem, value); }
    }
}