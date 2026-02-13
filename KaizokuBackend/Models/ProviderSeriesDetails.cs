using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderSeriesDetails : SeriesProviderDetailsBase
{
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
    [JsonPropertyName("fromChapter")]
    public decimal? ContinueAfterChapter { get; set; }
    [JsonPropertyName("isUnknown")]
    public bool IsUnknown { get; set; }

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

    [JsonPropertyName("isSelected")]
    public bool IsSelected { get; set; }

}
