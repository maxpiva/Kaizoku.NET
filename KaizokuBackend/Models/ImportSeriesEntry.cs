using System;
using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Enums;
using Action = KaizokuBackend.Models.Action;

namespace KaizokuBackend.Models;

public class ImportSeriesEntry : ImportSummaryBase
{
    private readonly ImportSeriesResult _series = new();
    [JsonPropertyName("path")]
    public required string Path
    {
        get => NormalizedPath;
        set => NormalizedPath = value;
    }
    [JsonPropertyName("title")]
    public required string Title
    {
        get => _series.Title;
        set => _series.Title = value;
    }
    [JsonPropertyName("status")]
    public ImportStatus Status { get; set; } = ImportStatus.Import;
    [JsonPropertyName("continueAfterChapter")]
    public decimal? ContinueAfterChapter { get; set; }
    [JsonPropertyName("action")]
    public Action Action { get; set; }
    [JsonPropertyName("series")]
    public List<ProviderSeriesOption>? Series { get; set; } = [];

    [JsonPropertyName("artist")]
    public string Artist
    {
        get => _series.Artist;
        set => _series.Artist = value;
    }

    [JsonPropertyName("author")]
    public string Author
    {
        get => _series.Author;
        set => _series.Author = value;
    }

    [JsonPropertyName("description")]
    public string Description
    {
        get => _series.Description;
        set => _series.Description = value;
    }

    [JsonPropertyName("genre")]
    public List<string> Genre
    {
        get => _series.Genre;
        set => _series.Genre = value ?? [];
    }

    [JsonPropertyName("type")]
    public string Type
    {
        get => _series.Type;
        set => _series.Type = value;
    }

    [JsonPropertyName("chapterCount")]
    public int ChapterCount
    {
        get => _series.ChapterCount;
        set => _series.ChapterCount = value;
    }

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTime? LastUpdatedUTC
    {
        get => _series.LastUpdatedUTC;
        set => _series.LastUpdatedUTC = value;
    }

    [JsonPropertyName("providers")]
    public List<ImportProviderSnapshot> Providers
    {
        get => _series.Providers;
        set => _series.Providers = value ?? [];
    }

    [JsonPropertyName("seriesStatus")]
    public SeriesStatus SeriesStatus
    {
        get => _series.Status;
        set => _series.Status = value;
    }

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled
    {
        get => _series.IsDisabled;
        set => _series.IsDisabled = value;
    }

    [JsonPropertyName("kaizokuVersion")]
    public int KaizokuVersion
    {
        get => _series.KaizokuVersion;
        set => _series.KaizokuVersion = value;
    }

    [JsonIgnore]
    public ImportSeriesResult SeriesDetails => _series;

}