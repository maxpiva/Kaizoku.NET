using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class SearchSource
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "";
    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = "";
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";
}