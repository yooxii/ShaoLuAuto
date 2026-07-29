using ShaoLu.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ShaoLu.Services
{
    public class UserService : IUserService
    {
        private static readonly Lazy<IFreeSql> _fsql = new(() =>
        {
            string dbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoShaoLu");
            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            string dbPath = Path.Combine(dbDir, "users.db");

            return new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite, $"Data Source={dbPath}")
                .UseAutoSyncStructure(true)
                .Build();
        });

        private static IFreeSql Fsql => _fsql.Value;

        public User CurrentUser { get; private set; }
        public bool IsAdmin => CurrentUser != null && CurrentUser.Role == UserRole.Admin;

        public UserService()
        {
            // 触发数据库初始化（自动建表）
            _ = Fsql;
        }

        private static string GenerateSalt()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] saltBytes = new byte[16];
                rng.GetBytes(saltBytes);
                return Convert.ToBase64String(saltBytes);
            }
        }

        private static string HashPassword(string password, string salt)
        {
            byte[] saltBytes = Convert.FromBase64String(salt);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 10000, HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(32);
                return Convert.ToBase64String(hash);
            }
        }

        private static bool VerifyPassword(string password, string storedHash, string salt)
        {
            string computedHash = HashPassword(password, salt);
            byte[] computedBytes = Convert.FromBase64String(computedHash);
            byte[] storedBytes = Convert.FromBase64String(storedHash);
            return ConstantTimeEquals(computedBytes, storedBytes);
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= a[i] ^ b[i];
            return result == 0;
        }

        public bool Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            var user = Fsql.Select<User>()
                .Where(u => u.Username == username)
                .First();

            if (user == null)
                return false;

            if (!VerifyPassword(password, user.PasswordHash, user.Salt))
                return false;

            CurrentUser = user;
            return true;
        }

        public void Logout()
        {
            CurrentUser = null;
        }

        public List<User> GetAllUsers()
        {
            return Fsql.Select<User>()
                .OrderBy(u => u.CreatedAt)
                .ToList();
        }

        public bool AddUser(string username, string password, UserRole role)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            // 检查用户名是否已存在
            if (Fsql.Select<User>().Where(u => u.Username == username).Any())
                return false;

            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);
            var user = new User
            {
                Username = username,
                PasswordHash = hash,
                Salt = salt,
                Role = role,
                CreatedAt = DateTime.Now
            };

            Fsql.Insert(user).ExecuteAffrows();
            return true;
        }

        public bool DeleteUser(string username)
        {
            var user = Fsql.Select<User>()
                .Where(u => u.Username == username)
                .First();

            if (user == null)
                return false;

            // 不允许删除最后一个管理员
            if (user.Role == UserRole.Admin)
            {
                long adminCount = Fsql.Select<User>()
                    .Where(u => u.Role == UserRole.Admin)
                    .Count();
                if (adminCount <= 1)
                    return false;
            }

            Fsql.Delete<User>()
                .Where(u => u.Username == username)
                .ExecuteAffrows();

            // 如果删除的是当前登录用户，则注销
            if (CurrentUser?.Username == username)
                CurrentUser = null;

            return true;
        }

        public bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
                return false;

            var user = Fsql.Select<User>()
                .Where(u => u.Username == username)
                .First();

            if (user == null)
                return false;

            if (!VerifyPassword(oldPassword, user.PasswordHash, user.Salt))
                return false;

            var salt = GenerateSalt();
            user.PasswordHash = HashPassword(newPassword, salt);
            user.Salt = salt;

            Fsql.Update<User>()
                .SetSource(user)
                .ExecuteAffrows();

            return true;
        }

        public bool HasAnyAdmin()
        {
            return Fsql.Select<User>()
                .Where(u => u.Role == UserRole.Admin)
                .Any();
        }

        public bool Register(string username, string password, string adminUsername = null, string adminPassword = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            // 检查用户名是否已存在
            if (Fsql.Select<User>().Where(u => u.Username == username).Any())
                return false;

            bool hasAdmin = HasAnyAdmin();

            if (hasAdmin)
            {
                // 必须有管理员审批
                if (string.IsNullOrEmpty(adminUsername) || string.IsNullOrEmpty(adminPassword))
                    return false;

                var adminUser = Fsql.Select<User>()
                    .Where(u => u.Username == adminUsername && u.Role == UserRole.Admin)
                    .First();

                if (adminUser == null)
                    return false;

                if (!VerifyPassword(adminPassword, adminUser.PasswordHash, adminUser.Salt))
                    return false;
            }

            // 注册新用户（默认为管理员角色）
            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);
            var newUser = new User
            {
                Username = username,
                PasswordHash = hash,
                Salt = salt,
                Role = UserRole.Admin,
                CreatedAt = DateTime.Now
            };

            Fsql.Insert(newUser).ExecuteAffrows();
            return true;
        }
    }
}
