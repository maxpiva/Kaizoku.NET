using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderExtendedInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
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
    public long ChapterCount { get; set; }
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
    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }
    [JsonPropertyName("isUninstalled")]
    public bool IsUninstalled { get; set; }
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; } = false;
    [JsonPropertyName("lastUpdatedUTC")]
    public DateTime LastUpdatedUTC { get; set; }
       
    [JsonPropertyName("status")]
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    [JsonPropertyName("lastChapter")]
    public decimal? LastChapter { get; set; }
    [JsonPropertyName("lastChangeUTC")]
    public DateTime LastChangeUTC { get; set; }
    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    [JsonPropertyName("matchId")]
    public Guid MatchId { get; set; }


}