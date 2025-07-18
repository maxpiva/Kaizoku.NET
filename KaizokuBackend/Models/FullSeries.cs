using System.Text.Json.Serialization;
using KaizokuBackend.Models.Database;

namespace KaizokuBackend.Models;

public class FullSeries
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
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("genre")]
    public List<string> Genre { get; set; } = new List<string>();
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("chapterCount")]
    public int ChapterCount { get; set; }
    [JsonPropertyName("fromChapter")]
    public decimal? ContinueAfterChapter { get; set; }
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("meta")]
    public Dictionary<string, string> Meta { get; set; } = new();
    [JsonPropertyName("useCover")]
    public bool UseCover { get; set; }
    [JsonPropertyName("isStorage")]
    public bool IsStorage { get; set; }
    [JsonPropertyName("isUnknown")]
    public bool IsUnknown { get; set; }
    [JsonPropertyName("useTitle")]
    public bool UseTitle { get; set; }

    [JsonPropertyName("existingProvider")]
    public bool ExistingProvider { get; set; }

    [JsonPropertyName("lastUpdatedUTC")]
    public DateTime LastUpdatedUTC { get; set; }
    [JsonPropertyName("suggestedFilename")]
    public string SuggestedFilename { get; set; } = "";

    [JsonPropertyName("chapters")]
    public List<Chapter> Chapters { get; set; } = [];
    [JsonPropertyName("status")]
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;

    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    [JsonPropertyName("isSelected")]
    public bool IsSelected { get; set; }

}