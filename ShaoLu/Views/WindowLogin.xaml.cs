using ShaoLu.Viewmodels;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Views
{
    public partial class WindowLogin : Window
    {
        private readonly LoginViewModel _viewModel;

        public WindowLogin(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.RequestClose += OnRequestClose;

            // 按 Enter 键触发登录（仅在登录模式下），按 Escape 关闭窗口
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _viewModel.CancelCommand.Execute(null);
                }
                else if (e.Key == Key.Enter)
                {
                    if (!_viewModel.IsRegisterMode && _viewModel.LoginCommand.CanExecute(null))
                    {
                        _viewModel.LoginCommand.Execute(null);
                    }
                    else if (_viewModel.IsRegisterMode && _viewModel.RegisterCommand.CanExecute(null))
                    {
                        _viewModel.RegisterCommand.Execute(null);
                    }
                }
            };

            UsernameBox.Focus();
        }

        private void OnRequestClose(bool success)
        {
            DialogResult = success;
            Close();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = PasswordBox.Password;
        }

        private void RegisterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.RegisterPassword = RegisterPasswordBox.Password;
        }

        private void RegisterConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.RegisterConfirmPassword = RegisterConfirmPasswordBox.Password;
        }

        private void AdminPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.AdminPassword = AdminPasswordBox.Password;
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // 命令已通过绑定执行，此处无需额外处理
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelCommand.Execute(null);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // 支持拖动窗口（WindowStyle=None）
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
