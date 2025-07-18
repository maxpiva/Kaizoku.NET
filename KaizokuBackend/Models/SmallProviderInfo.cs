using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class SmallProviderInfo
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("scanlator")]
    public string Scanlator { get; set; } = string.Empty;
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
    [JsonPropertyName("url")]
    public string? Url { get; set; } = string.Empty;
    [JsonPropertyName("isStorage")]
    public bool IsStorage { get; set; } = false;
}