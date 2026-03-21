using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class LoginDto
    {
        [JsonPropertyName("usernameOrEmail")]
        public string UsernameOrEmail { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("rememberMe")]
        public bool RememberMe { get; set; } = false;
    }
}
