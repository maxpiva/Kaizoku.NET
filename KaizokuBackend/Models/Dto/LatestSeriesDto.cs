using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

// [Schema] // Controller I/O Model
public class LatestSeriesDto : IThumb
{
    [Key]

    [JsonPropertyName("mihonId")]
    public string MihonId { get; set; }
    [JsonPropertyName("mihonProviderId")]
    public string? MihonProviderId { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; } = null;
    [JsonPropertyName("artist")]
    public string? Artist { get; set; } = null;
    [JsonPropertyName("author")]
    public string? Author { get; set; } = null;
    [JsonPropertyName("description")]
    public string? Description { get; set; } = null;
    [JsonPropertyName("genre")]
    public List<string> Genre { get; set; } = new();
    [JsonPropertyName("fetchDate")]
    public DateTime FetchDate { get; set; }
    [JsonPropertyName("chapterCount")]
    public long? ChapterCount { get; set; } = null;
    [JsonPropertyName("latestChapter")]
    public decimal? LatestChapter { get; set; }
    [JsonPropertyName("latestChapterTitle")]
    public string LatestChapterTitle { get; set; } = "";
    [JsonPropertyName("status")]
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    [JsonPropertyName("inLibrary")]
    public InLibraryStatus InLibrary { get; set; } = InLibraryStatus.NotInLibrary;
    [JsonPropertyName("seriesId")]
    public Guid? SeriesId { get; set; }

}