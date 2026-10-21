using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ShaoLu.Viewmodels
{
    public class UserManagementViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private static string Loc(string key) => LanguageService.GetLocalizedString(key);

        /// <summary>角色下拉索引：0=管理员，1=普通用户（与 WindowUserManagement.xaml 的 ComboBox 顺序一致）</summary>
        private const int AdminRoleIndex = 0;
        private const int UserRoleIndex = 1;

        /// <summary>新增用户默认角色索引：普通用户（不得默认创建管理员）</summary>
        private const int DefaultRoleIndex = UserRoleIndex;

        /// <summary>非管理员执行管理操作时的提示</summary>
        private static readonly string AdminRequiredMessage = LanguageService.GetLocalizedString("Msg_AdminRequired", "需要管理员权限才能管理用户");

        public ObservableCollection<User> Users { get; } = new ObservableCollection<User>();

        private User _selectedUser;
        public User SelectedUser
        {
            get => _selectedUser;
            set => SetProperty(ref _selectedUser, value);
        }

        // 添加用户表单
        private string _newUsername = string.Empty;
        public string NewUsername
        {
            get => _newUsername;
            set => SetProperty(ref _newUsername, value);
        }

        private string _newPassword = string.Empty;
        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        private int _newRoleIndex = DefaultRoleIndex;
        public int NewRoleIndex
        {
            get => _newRoleIndex;
            set => SetProperty(ref _newRoleIndex, value);
        }

        // 修改密码表单
        private string _oldPassword = string.Empty;
        public string OldPassword
        {
            get => _oldPassword;
            set => SetProperty(ref _oldPassword, value);
        }

        private string _newChangePassword = string.Empty;
        public string NewChangePassword
        {
            get => _newChangePassword;
            set => SetProperty(ref _newChangePassword, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private RelayCommand _addUserCommand;
        public RelayCommand AddUserCommand => _addUserCommand ??= new RelayCommand(ExecuteAddUser);

        private RelayCommand _deleteUserCommand;
        public RelayCommand DeleteUserCommand => _deleteUserCommand ??= new RelayCommand(ExecuteDeleteUser);

        private RelayCommand _changePasswordCommand;
        public RelayCommand ChangePasswordCommand => _changePasswordCommand ??= new RelayCommand(ExecuteChangePassword);

        private RelayCommand _refreshCommand;
        public RelayCommand RefreshCommand => _refreshCommand ??= new RelayCommand(LoadUsers);

        public UserManagementViewModel(IUserService userService)
        {
            _userService = userService;
            LoadUsers();
        }

        private void LoadUsers()
        {
            Users.Clear();
            foreach (var user in _userService.GetAllUsers())
            {
                Users.Add(user);
            }
            StatusMessage = string.Empty;
        }

        private void ExecuteAddUser()
        {
            StatusMessage = string.Empty;

            // 管理操作前置校验：只有管理员可以新增用户
            if (!_userService.IsAdmin)
            {
                StatusMessage = AdminRequiredMessage;
                return;
            }

            if (string.IsNullOrWhiteSpace(NewUsername))
            {
                StatusMessage = Loc("Error_EmptyUsername");
                return;
            }
            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                StatusMessage = Loc("Error_EmptyPassword");
                return;
            }

            // 角色：0=管理员，其余一律普通用户（默认普通用户，不默认创建管理员）
            UserRole role = NewRoleIndex == AdminRoleIndex ? UserRole.Admin : UserRole.User;

            if (_userService.AddUser(NewUsername, NewPassword, role))
            {
                StatusMessage = string.Format(Loc("Msg_UserAdded"), NewUsername);
                NewUsername = string.Empty;
                NewPassword = string.Empty;
                NewRoleIndex = DefaultRoleIndex;
                LoadUsers();
            }
            else
            {
                StatusMessage = string.Format(Loc("Msg_UserAddFailed"), NewUsername);
            }
        }

        private void ExecuteDeleteUser()
        {
            StatusMessage = string.Empty;

            // 管理操作前置校验：只有管理员可以删除用户
            if (!_userService.IsAdmin)
            {
                StatusMessage = AdminRequiredMessage;
                return;
            }

            if (SelectedUser == null)
            {
                StatusMessage = Loc("Msg_SelectUserFirst");
                return;
            }

            if (SelectedUser.Username == _userService.CurrentUser?.Username)
            {
                StatusMessage = Loc("Msg_CannotDeleteSelf");
                return;
            }

            var result = MessageBox.Show(
                string.Format(Loc("Msg_ConfirmDeleteUser"), SelectedUser.Username),
                Loc("ConfirmDeleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (_userService.DeleteUser(SelectedUser.Username))
                {
                    StatusMessage = LanguageService.GetLocalizedString("Msg_UserDeleted", $"用户 '{SelectedUser.Username}' 已删除", SelectedUser.Username);
                    LoadUsers();
                }
                else
                {
                    StatusMessage = LanguageService.GetLocalizedString("Msg_DeleteLastAdminFailed", "删除失败（可能是最后一个管理员账户）");
                }
            }
        }

        private void ExecuteChangePassword()
        {
            StatusMessage = string.Empty;

            if (SelectedUser == null)
            {
                StatusMessage = LanguageService.GetLocalizedString("Msg_SelectUserForPassword", "请先选择要修改密码的用户");
                return;
            }
            // 修改他人密码属于管理操作：只有管理员可以执行
            if (!_userService.IsAdmin && SelectedUser.Username != _userService.CurrentUser?.Username)
            {
                StatusMessage = AdminRequiredMessage;
                return;
            }
            if (string.IsNullOrWhiteSpace(OldPassword))
            {
                StatusMessage = LanguageService.GetLocalizedString("Msg_EnterOldPassword", "请输入旧密码");
                return;
            }
            if (string.IsNullOrWhiteSpace(NewChangePassword))
            {
                StatusMessage = LanguageService.GetLocalizedString("Msg_EnterNewPassword", "请输入新密码");
                return;
            }

            if (_userService.ChangePassword(SelectedUser.Username, OldPassword, NewChangePassword))
            {
                StatusMessage = LanguageService.GetLocalizedString("Msg_PasswordChanged", $"用户 '{SelectedUser.Username}' 的密码已修改", SelectedUser.Username);
                OldPassword = string.Empty;
                NewChangePassword = string.Empty;
            }
            else
            {
                StatusMessage = LanguageService.GetLocalizedString("Msg_PasswordChangeFailed", "密码修改失败，请检查旧密码是否正确");
            }
        }
    }
}
