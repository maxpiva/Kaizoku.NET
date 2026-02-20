using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.Models;

public record TachiyomiExtension
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("pkg")]
    public string Package { get; set; }
    [JsonPropertyName("apk")]
    public string Apk { get; set; }
    [JsonPropertyName("lang")]
    public string Language { get; set; }
    [JsonPropertyName("code")]
    public int VersionCode { get; set; }
    [JsonPropertyName("version")]
    public string Version { get; set; }
    [JsonPropertyName("nsfw")]
    public int Nsfw { get; set; }
    [JsonPropertyName("sources")]
    public List<TachiyomiSource> Sources { get; set; } = [];
}
