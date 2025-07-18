using System.Text.Json.Serialization;

namespace KaizokuBackend.Models
{
    public class ProviderMatch
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
        [JsonPropertyName("matchInfos")]
        public List<MatchInfo> MatchInfos { get; set; } = [];
        [JsonPropertyName("chapters")]
        public List<ProviderMatchChapter> Chapters { get; set; } = [];
    }
}
