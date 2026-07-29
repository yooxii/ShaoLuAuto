using ShaoLu.Viewmodels;
using System.Windows;

namespace ShaoLu.Views
{
    public partial class WindowUserManagement : Window
    {
        private readonly UserManagementViewModel _viewModel;

        public WindowUserManagement(UserManagementViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void AddPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.NewPassword = AddPasswordBox.Password;
        }

        private void OldPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.OldPassword = OldPasswordBox.Password;
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.NewChangePassword = NewPasswordBox.Password;
        }
    }
}
