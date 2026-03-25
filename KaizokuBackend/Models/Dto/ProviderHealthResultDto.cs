using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

/// <summary>
/// Result of a provider health check
/// </summary>
public class ProviderHealthResultDto
{
    [JsonPropertyName("mihonProviderId")]
    public string MihonProviderId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("checkedAtUtc")]
    public DateTime CheckedAtUtc { get; set; }
}
