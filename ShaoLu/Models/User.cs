using FreeSql.DataAnnotations;
using System;

namespace ShaoLu.Models
{
    public enum UserRole
    {
        Admin,
        User
    }

    [Table(Name = "app_user")]
    public class User
    {
        [Column(IsIdentity = true, IsPrimary = true)]
        public int Id { get; set; }

        [Column(StringLength = 128, IsNullable = false)]
        public string Username { get; set; }

        [Column(StringLength = 256)]
        public string PasswordHash { get; set; }

        [Column(StringLength = 64)]
        public string Salt { get; set; }

        [Column(MapType = typeof(string))]
        public UserRole Role { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
