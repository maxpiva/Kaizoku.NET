using KaizokuBackend.Extensions;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

/// <summary>
/// Adds provider-specific projection metadata shared by multiple series DTOs.
/// </summary>
public abstract class SeriesProviderDetailsBase : SeriesSummaryBase
{
    private long _chapterCount;
    private string _chapterList = string.Empty;
    private bool _useTitle;

    [JsonPropertyName("scanlator")]
    public string Scanlator { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }
        = null;

    [JsonPropertyName("chapterCount")]
    public long ChapterCount
    {
        get => _chapterCount;
        set => _chapterCount = SeriesModelExtensions.ClampChapterCount(value);
    }

    [JsonPropertyName("chapterList")]
    public string ChapterList
    {
        get => _chapterList;
        set => _chapterList = value ?? string.Empty;
    }

    [JsonPropertyName("useTitle")]
    public bool UseTitle
    {
        get => _useTitle;
        set => _useTitle = value;
    }
}
