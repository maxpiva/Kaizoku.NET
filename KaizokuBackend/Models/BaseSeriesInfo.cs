using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models;



public class BaseSeriesInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = string.Empty;
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("genre")]
    public List<string> Genre { get; set; } = new List<string>();
    [JsonPropertyName("status")]
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    
    
    private string _storagePath = string.Empty;

    [JsonPropertyName("storagePath")]
    public string StoragePath
    {
        get => _storagePath.SanitizeDirectory();
        set => _storagePath = value;
    }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("chapterCount")]
    public int ChapterCount { get; set; }
    [JsonPropertyName("lastChapter")]
    public decimal? LastChapter { get; set; }
    [JsonPropertyName("lastChangeUTC")]
    public DateTime? LastChangeUTC { get; set; }
    [JsonPropertyName("lastChangeProvider")]
    public SmallProviderInfo LastChangeProvider { get; set; } = new SmallProviderInfo();
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;
    [JsonPropertyName("pausedDownloads")]
    public bool PausedDownloads { get; set; }
}