using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

/// <summary>
/// Request DTO for setting the active version of an installed extension.
/// </summary>
public class SetProviderVersionRequestDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("autoUpdate")]
    public bool AutoUpdate { get; set; } = true;
}
