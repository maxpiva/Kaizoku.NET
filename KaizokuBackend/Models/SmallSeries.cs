using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class SmallSeries
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "";
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";
    [JsonPropertyName("scanlator")]
    public string Scanlator { get; set; } = "";
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "";
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; } = null;
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("chapterCount")]
    public long ChapterCount { get; set; }
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("lastChapter")]
    public decimal? LastChapter { get; set; }

    [JsonPropertyName("preferred")]
    public bool Preferred { get; set; }

    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    [JsonPropertyName("useCover")]
    public bool UseCover { get; set; }
    [JsonPropertyName("isStorage")]
    public bool IsStorage { get; set; }
    [JsonPropertyName("useTitle")]
    public bool UseTitle { get; set; }



}