using System.Text.Json.Serialization;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class UserDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public UserRole Role { get; set; }

        [JsonPropertyName("avatarPath")]
        public string? AvatarPath { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("lastLoginAt")]
        public DateTime? LastLoginAt { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    public class UserDetailDto : UserDto
    {
        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("permissions")]
        public PermissionDto Permissions { get; set; } = new();

        [JsonPropertyName("preferences")]
        public UserPreferencesDto Preferences { get; set; } = new();
    }

    public class UpdateUserDto
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public UserRole? Role { get; set; }

        [JsonPropertyName("isActive")]
        public bool? IsActive { get; set; }
    }

    public class ChangePasswordDto
    {
        [JsonPropertyName("currentPassword")]
        public string CurrentPassword { get; set; } = string.Empty;

        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
