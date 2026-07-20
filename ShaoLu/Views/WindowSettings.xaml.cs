using ShaoLu.Viewmodels;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowSettings.xaml 的交互逻辑
    /// </summary>
    public partial class WindowSettings : Window
    {
        public WindowSettings()
        {
            InitializeComponent();
            DataContext = new SettingsWindowViewModel();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is SettingsCategory category && DataContext is SettingsWindowViewModel vm)
            {
                vm.SelectedCategory = category;
            }
        }
    }
}
