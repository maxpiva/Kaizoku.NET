using System;
using System.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mihon.ExtensionsBridge.Models;

public record TachiyomiExtension
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("pkg")]
    public string Package { get; set; }
    [JsonPropertyName("icon")]
    public string Icon { get; set; }
    [JsonPropertyName("apk")]
    public string Apk { get; set; }
    [JsonPropertyName("jar")]
    public string Jar { get; set; }
    [JsonPropertyName("lang")]
    public string Language { get; set; }
    [JsonPropertyName("code")]
    public int VersionCode { get; set; }
    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("extensionlib")]
    public string ExtensionLib { get; set; }
    [JsonPropertyName("nsfw")]
    public int Nsfw { get; set; }
    [JsonPropertyName("mixed")]
    public int Mixed { get; set; }
    [JsonPropertyName("sources")]    
    public List<TachiyomiSource> Sources { get; set; } = [];
}
public record TachiyomiExtensionV2
{
    public string name { get; set; }
    public string packageName { get; set; }
    public Resources resources { get; set; }
    public string extensionLib { get; set; }
    public string versionCode { get; set; }
    public string versionName { get; set; }
    public string contentWarning { get; set; }
    public List<TachiyomiSourceV2> sources { get; set; }
}
