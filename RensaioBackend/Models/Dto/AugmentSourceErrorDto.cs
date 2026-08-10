using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto
{
    /// <summary>
    /// Describes why a selected source could not contribute series details during augmentation.
    /// </summary>
    public class AugmentSourceErrorDto
    {
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
