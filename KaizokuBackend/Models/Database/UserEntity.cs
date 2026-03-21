using System.ComponentModel.DataAnnotations;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models.Database
{
    public class UserEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.User;
        public string? AvatarPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual UserPermissionEntity? Permissions { get; set; }
        public virtual UserPreferencesEntity? Preferences { get; set; }
        public virtual ICollection<UserSessionEntity> Sessions { get; set; } = [];
    }
}
