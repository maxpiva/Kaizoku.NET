using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class UserPreferencesDto
    {
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";

        [JsonPropertyName("defaultLanguage")]
        public string DefaultLanguage { get; set; } = "en";

        [JsonPropertyName("cardSize")]
        public string CardSize { get; set; } = "medium";

        [JsonPropertyName("nsfwVisibility")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NsfwVisibility NsfwVisibility { get; set; } = NsfwVisibility.HideByDefault;
    }

    public class UpdatePreferencesDto : UserPreferencesDto
    {
    }
}
