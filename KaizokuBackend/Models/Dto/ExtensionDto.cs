using KaizokuBackend.Models.Abstractions;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;
/// <summary>
/// Model class to represent an extension
/// </summary>
public class ExtensionDto : IThumb
{
    [JsonPropertyName("package")]
    public string Package { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; }
    [JsonPropertyName("isStorage")]
    public bool IsStorage { get; set; } = true;
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("isBroken")]
    public bool IsBroken { get; set; } = false;
    [JsonPropertyName("isDead")]
    public bool IsDead { get; set; } = false;
    [JsonPropertyName("isInstaled")]
    public bool IsInstaled { get; set; } = false;
    [JsonPropertyName("activeEntry")]
    public int ActiveEntry { get; set; }
    [JsonPropertyName("autoUpdate")]
    public bool AutoUpdate { get; set; } = true;
    [JsonPropertyName("onlineRepositories")]
    public List<ExtensionRepositoryDto> Repositories { get; set; } = [];
}
