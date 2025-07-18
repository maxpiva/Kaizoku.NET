using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

// [Schema] // Controller I/O Model
public class LinkedSeries 
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "";
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "";
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; } = null;
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("linkedIds")]
    public List<string> LinkedIds { get; set; } = new List<string>();
    [JsonPropertyName("useCover")]
    public bool UseCover { get; set; }
    [JsonPropertyName("isStorage")]
    public bool IsStorage { get; set; }
}