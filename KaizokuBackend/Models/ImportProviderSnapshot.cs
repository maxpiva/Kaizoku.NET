using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ImportProviderSnapshot : ProviderSummaryBase
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailUrl")]
    public override string? ThumbnailUrl { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public override SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public int ChapterCount { get; set; }
    public List<StartStop> ChapterList { get; set; } = [];
    public bool IsDisabled { get; set; }
    public List<ProviderArchiveSnapshot> Archives { get; set; } = [];

}