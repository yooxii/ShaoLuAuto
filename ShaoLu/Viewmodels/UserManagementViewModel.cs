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

        private int _newRoleIndex;
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

            UserRole role = NewRoleIndex == 0 ? UserRole.Admin : UserRole.User;

            if (_userService.AddUser(NewUsername, NewPassword, role))
            {
                StatusMessage = string.Format(Loc("Msg_UserAdded"), NewUsername);
                NewUsername = string.Empty;
                NewPassword = string.Empty;
                NewRoleIndex = 0;
                LoadUsers();
            }
            else
            {
                StatusMessage = string.Format(Loc("Msg_UserAddFailed"), NewUsername);
            }
        }

        private void ExecuteDeleteUser()
        {
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
                    StatusMessage = $"用户 '{SelectedUser.Username}' 已删除";
                    LoadUsers();
                }
                else
                {
                    StatusMessage = "删除失败（可能是最后一个管理员账户）";
                }
            }
        }

        private void ExecuteChangePassword()
        {
            StatusMessage = string.Empty;

            if (SelectedUser == null)
            {
                StatusMessage = "请先选择要修改密码的用户";
                return;
            }
            if (string.IsNullOrWhiteSpace(OldPassword))
            {
                StatusMessage = "请输入旧密码";
                return;
            }
            if (string.IsNullOrWhiteSpace(NewChangePassword))
            {
                StatusMessage = "请输入新密码";
                return;
            }

            if (_userService.ChangePassword(SelectedUser.Username, OldPassword, NewChangePassword))
            {
                StatusMessage = $"用户 '{SelectedUser.Username}' 的密码已修改";
                OldPassword = string.Empty;
                NewChangePassword = string.Empty;
            }
            else
            {
                StatusMessage = "密码修改失败，请检查旧密码是否正确";
            }
        }
    }
}
