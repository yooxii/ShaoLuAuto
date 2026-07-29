using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Services;
using System;

namespace ShaoLu.Viewmodels
{
    public class LoginViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private static string Loc(string key) => LanguageService.GetLocalizedString(key);

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsLoginSuccessful { get; private set; }

        /// <summary>
        /// 请求关闭窗口的事件（由 View 订阅）
        /// </summary>
        public event Action<bool> RequestClose;

        private RelayCommand _loginCommand;
        public RelayCommand LoginCommand => _loginCommand ??= new RelayCommand(ExecuteLogin);

        private RelayCommand _cancelCommand;
        public RelayCommand CancelCommand => _cancelCommand ??= new RelayCommand(ExecuteCancel);

        public LoginViewModel(IUserService userService)
        {
            _userService = userService;
        }

        private void ExecuteCancel()
        {
            RequestClose?.Invoke(false);
        }

        private void ExecuteLogin()
        {
            ErrorMessage = null;

            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = Loc("Error_EmptyUsername");
                return;
            }

            // 密码由 View code-behind 通过 Password 属性传入
            if (string.IsNullOrEmpty(Password))
            {
                ErrorMessage = Loc("Error_EmptyPassword");
                return;
            }

            if (_userService.Login(Username, Password))
            {
                IsLoginSuccessful = true;
                RequestClose?.Invoke(true);
            }
            else
            {
                ErrorMessage = Loc("Error_LoginFailed");
            }
        }

        /// <summary>
        /// 由 View code-behind 设置的密码（PasswordBox 不支持直接绑定）
        /// </summary>
        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        // ===== 注册相关 =====

        private bool _isRegisterMode;
        public bool IsRegisterMode
        {
            get => _isRegisterMode;
            set
            {
                if (SetProperty(ref _isRegisterMode, value))
                {
                    ErrorMessage = null;
                    RegisterErrorMessage = null;
                }
            }
        }

        private string _registerUsername = string.Empty;
        public string RegisterUsername
        {
            get => _registerUsername;
            set => SetProperty(ref _registerUsername, value);
        }

        private string _registerPassword = string.Empty;
        public string RegisterPassword
        {
            get => _registerPassword;
            set => SetProperty(ref _registerPassword, value);
        }

        private string _registerConfirmPassword = string.Empty;
        public string RegisterConfirmPassword
        {
            get => _registerConfirmPassword;
            set => SetProperty(ref _registerConfirmPassword, value);
        }

        private string _adminUsername = string.Empty;
        public string AdminUsername
        {
            get => _adminUsername;
            set => SetProperty(ref _adminUsername, value);
        }

        private string _adminPassword = string.Empty;
        public string AdminPassword
        {
            get => _adminPassword;
            set => SetProperty(ref _adminPassword, value);
        }

        private string _registerErrorMessage;
        public string RegisterErrorMessage
        {
            get => _registerErrorMessage;
            set => SetProperty(ref _registerErrorMessage, value);
        }

        private string _registerSuccessMessage;
        public string RegisterSuccessMessage
        {
            get => _registerSuccessMessage;
            set => SetProperty(ref _registerSuccessMessage, value);
        }

        /// <summary>
        /// 是否需要管理员审批（系统中是否已有管理员）
        /// </summary>
        private bool _needAdminApproval;
        public bool NeedAdminApproval
        {
            get => _needAdminApproval;
            set => SetProperty(ref _needAdminApproval, value);
        }

        private RelayCommand _registerCommand;
        public RelayCommand RegisterCommand => _registerCommand ??= new RelayCommand(ExecuteRegister);

        private RelayCommand _switchToRegisterCommand;
        public RelayCommand SwitchToRegisterCommand => _switchToRegisterCommand ??= new RelayCommand(() =>
        {
            NeedAdminApproval = _userService.HasAnyAdmin();
            IsRegisterMode = true;
        });

        private RelayCommand _switchToLoginCommand;
        public RelayCommand SwitchToLoginCommand => _switchToLoginCommand ??= new RelayCommand(() => IsRegisterMode = false);

        private void ExecuteRegister()
        {
            RegisterErrorMessage = null;
            RegisterSuccessMessage = null;

            if (string.IsNullOrWhiteSpace(RegisterUsername))
            {
                RegisterErrorMessage = Loc("Error_EmptyUsername");
                return;
            }

            if (string.IsNullOrEmpty(RegisterPassword))
            {
                RegisterErrorMessage = Loc("Error_EmptyPassword");
                return;
            }

            if (RegisterPassword != RegisterConfirmPassword)
            {
                RegisterErrorMessage = Loc("Error_PasswordMismatch");
                return;
            }

            if (RegisterPassword.Length < 3)
            {
                RegisterErrorMessage = Loc("Error_PasswordTooShort");
                return;
            }

            if (NeedAdminApproval)
            {
                if (string.IsNullOrWhiteSpace(AdminUsername))
                {
                    RegisterErrorMessage = Loc("Error_EmptyAdminUsername");
                    return;
                }

                if (string.IsNullOrEmpty(AdminPassword))
                {
                    RegisterErrorMessage = Loc("Error_EmptyAdminPassword");
                    return;
                }
            }

            bool success = _userService.Register(
                RegisterUsername,
                RegisterPassword,
                NeedAdminApproval ? AdminUsername : null,
                NeedAdminApproval ? AdminPassword : null
            );

            if (success)
            {
                RegisterSuccessMessage = Loc("Register_Success");
                // 保存注册的用户名，用于填充登录表单
                string registeredUsername = RegisterUsername;
                // 清除注册表单
                RegisterUsername = string.Empty;
                RegisterPassword = string.Empty;
                RegisterConfirmPassword = string.Empty;
                AdminUsername = string.Empty;
                AdminPassword = string.Empty;

                // 切换到登录模式，并预填用户名
                Username = registeredUsername;
                IsRegisterMode = false;
            }
            else
            {
                RegisterErrorMessage = NeedAdminApproval
                    ? Loc("Error_RegisterFailedAdmin")
                    : Loc("Error_RegisterFailed");
            }
        }
    }
}
