using ShaoLu.Models;
using System.Collections.Generic;

namespace ShaoLu.Services
{
    public interface IUserService
    {
        User CurrentUser { get; }
        bool IsAdmin { get; }
        bool Login(string username, string password);
        void Logout();
        List<User> GetAllUsers();
        bool AddUser(string username, string password, UserRole role);
        bool DeleteUser(string username);
        bool ChangePassword(string username, string oldPassword, string newPassword);
        bool HasAnyAdmin();
        bool Register(string username, string password, string adminUsername = null, string adminPassword = null);
    }
}
