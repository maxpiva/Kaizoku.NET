using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.Models;

public record TachiyomiSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("lang")]
    public string Language { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("BaseUrl")]
    public string BaseUrl { get; set; }
    [JsonPropertyName("versionId")]
    public int VersionId { get; set; }
}




