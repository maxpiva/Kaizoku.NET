using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;


public class SuwayomiSeries 
{
    public int Id { get; set; }
    public string SourceId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; } = null;
    public long ThumbnailUrlLastFetched { get; set; } = 0;
    public bool Initialized { get; set; } = false;
    public string? Artist { get; set; } = null;
    public string? Author { get; set; } = null;
    public string? Description { get; set; } = null;
    public List<string> Genre { get; set; } = new();
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SeriesStatus Status { get; set; }
    public bool InLibrary { get; set; } = false;
    public long InLibraryAt { get; set; } = 0;
    public Dictionary<string, string> Meta { get; set; } = new();
    public string? RealUrl { get; set; } = null;
    public long? LastFetchedAt { get; set; } = 0;
    public long? ChaptersLastFetchedAt { get; set; } = 0;
    public bool FreshData { get; set; } = false;
    public long? UnreadCount { get; set; } = null;
    public long? DownloadCount { get; set; } = null;
    public long? ChapterCount { get; set; } = null;
    public long? LastReadAt { get; set; } = null;
    public List<SuwayomiChapter> Chapters { get; set; } = new();
}