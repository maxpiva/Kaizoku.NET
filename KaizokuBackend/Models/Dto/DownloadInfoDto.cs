using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;
using KaizokuBackend.Models;

namespace KaizokuBackend.Models.Dto;

public class DownloadInfoDto : DownloadSummaryBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("chapter")]
    public decimal? Chapter { get; set; }
    [JsonPropertyName("chapterTitle")]
    public string? ChapterTitle { get; set; }
    [JsonPropertyName("downloadDateUTC")]
    public DateTime? DownloadDateUTC { get; set; }
    [JsonPropertyName("status")]
    public QueueStatus Status { get; set; }
    [JsonPropertyName("scheduledDateUTC")]
    public DateTime ScheduledDateUTC { get; set; }
    [JsonPropertyName("retries")]
    public int Retries { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; } = null;
}