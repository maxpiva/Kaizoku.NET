using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class MatchInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";
    [JsonPropertyName("scanlator")]
    public string Scanlator { get; set; } = "";
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";
}