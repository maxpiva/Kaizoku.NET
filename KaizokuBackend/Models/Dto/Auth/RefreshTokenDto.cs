using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class RefreshTokenDto
    {
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class CreateUserDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public KaizokuBackend.Models.Enums.UserRole Role { get; set; } = KaizokuBackend.Models.Enums.UserRole.User;

        [JsonPropertyName("permissions")]
        public PermissionDto? Permissions { get; set; }
    }

    public class ResetPasswordDto
    {
        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
