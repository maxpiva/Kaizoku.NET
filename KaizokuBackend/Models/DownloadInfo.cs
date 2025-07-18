using KaizokuBackend.Models.Database;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class DownloadInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";
    [JsonPropertyName("scanlator")]
    public string? Scanlator { get; set; }
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

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

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; } = null;

    [JsonPropertyName("url")]
    public string? Url { get; set; } = null;
}