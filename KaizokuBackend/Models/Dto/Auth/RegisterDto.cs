using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class RegisterDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("inviteCode")]
        public string InviteCode { get; set; } = string.Empty;
    }
}
