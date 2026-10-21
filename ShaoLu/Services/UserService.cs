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

        /// <summary>
        /// 当前登录用户是否为管理员（用户管理、删除用户、修改角色等管理操作必须先校验）
        /// </summary>
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

        /// <summary>
        /// 新增用户（管理操作）：要求当前登录用户为管理员。
        /// role 为调用方显式指定，缺省语义为普通用户（UserRole.User）。
        /// </summary>
        public bool AddUser(string username, string password, UserRole role)
        {
            if (!IsAdmin)
                return false;

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

            return Fsql.Insert(user).ExecuteAffrows() > 0;
        }

        /// <summary>
        /// 删除用户（管理操作）：要求当前登录用户为管理员，且不允许删除最后一个管理员。
        /// </summary>
        public bool DeleteUser(string username)
        {
            if (!IsAdmin)
                return false;

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

        /// <summary>
        /// 修改用户角色（管理操作）：要求当前登录用户为管理员，且不允许把最后一个管理员降级。
        /// </summary>
        public bool ChangeRole(string username, UserRole role)
        {
            if (!IsAdmin || string.IsNullOrEmpty(username))
                return false;

            var user = Fsql.Select<User>()
                .Where(u => u.Username == username)
                .First();
            if (user == null)
                return false;

            if (user.Role == role)
                return true;

            // 不允许把最后一个管理员降级
            if (user.Role == UserRole.Admin && role != UserRole.Admin)
            {
                long adminCount = Fsql.Select<User>()
                    .Where(u => u.Role == UserRole.Admin)
                    .Count();
                if (adminCount <= 1)
                    return false;
            }

            user.Role = role;
            Fsql.Update<User>()
                .SetSource(user)
                .ExecuteAffrows();

            // 当前用户角色变化后同步内存中的登录信息
            if (CurrentUser?.Username == username)
                CurrentUser.Role = role;

            return true;
        }

        /// <summary>
        /// 修改密码：本人修改需校验旧密码；管理员可为其他用户重置密码。
        /// </summary>
        public bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
                return false;

            // 非本人且非管理员：拒绝
            if (!IsAdmin && CurrentUser?.Username != username)
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

        /// <summary>
        /// 自助注册。
        /// 首次注册（系统中还没有管理员）时创建第一个管理员，作为系统引导；
        /// 已有管理员时必须由管理员审批，且新用户固定为普通用户（UserRole.User），
        /// 自助注册不得再创建管理员。
        /// </summary>
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

            // 注册新用户：首个用户（引导管理员）为管理员，其余一律普通用户
            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);
            var newUser = new User
            {
                Username = username,
                PasswordHash = hash,
                Salt = salt,
                Role = hasAdmin ? UserRole.User : UserRole.Admin,
                CreatedAt = DateTime.Now
            };

            return Fsql.Insert(newUser).ExecuteAffrows() > 0;
        }
    }
}
