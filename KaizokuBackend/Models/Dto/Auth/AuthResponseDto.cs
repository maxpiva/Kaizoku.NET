using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class AuthResponseDto
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("user")]
        public UserDetailDto User { get; set; } = new();

        /// <summary>
        /// When true, the user's current password does not meet the updated password policy
        /// and they should be prompted to change it before proceeding.
        /// </summary>
        [JsonPropertyName("requiresPasswordChange")]
        public bool RequiresPasswordChange { get; set; }
    }
}
