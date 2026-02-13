using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class DownloadsMetricsDto
{
    [JsonPropertyName("downloads")]
    public int Downloads { get; set; } = 0;
    [JsonPropertyName("queued")]
    public int Queued { get; set; } = 0;
    [JsonPropertyName("failed")]
    public int Failed { get; set; } = 0;
}