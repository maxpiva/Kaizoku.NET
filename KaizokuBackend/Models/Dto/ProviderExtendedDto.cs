using KaizokuBackend.Models;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class ProviderExtendedDto : ProviderSummaryBase, IThumb
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
    [JsonPropertyName("lang")]
    public override string Language
    {
        get => base.Language;
        set => base.Language = value;
    }
    [JsonIgnore]
    public string Lang
    {
        get => Language;
        set => Language = value;
    }
    [JsonPropertyName("thumbnailUrl")]
    public override string? ThumbnailUrl { get; set; } = null;
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
    public override string? Url { get; set; }
    [JsonPropertyName("useCover")]
    public bool UseCover { get; set; }
    [JsonPropertyName("isUnknown")]
    public bool IsUnknown { get; set; }
    [JsonPropertyName("isLocal")]
    public bool IsLocal { get; set; }

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
    public override SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    [JsonPropertyName("lastChapter")]
    public decimal? LastChapter { get; set; }
    [JsonPropertyName("lastChangeUTC")]
    public DateTime LastChangeUTC { get; set; }
    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    [JsonPropertyName("matchId")]
    public Guid MatchId { get; set; }

}