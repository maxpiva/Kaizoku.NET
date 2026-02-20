using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Dto;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ChapterDownload : DownloadSummaryBase, IBridgeItemInfo
{
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public Guid SeriesProviderId { get; set; }
    public string BridgeItemInfo { get; set; }
    public string MihonId { get; set; }
    public string MihonProviderId { get; set; }
    public int Index { get; set; }
    public int PageCount { get; set; }
    public long? SourceId { get; set; }
    public string? MangaUrl { get; set; }
    public string? ChapterUrl { get; set; }
    [JsonIgnore]
    public string ProviderName
    {
        get => Provider;
        set => Provider = value;
    }
    public string ChapterName { get; set; } = string.Empty;
    public string SeriesTitle { get; set; } = "";

    public string? Url { get; set; } = null;
    public int Retries { get; set; }

    private string _storagePath = string.Empty;
    public string StoragePath
    {
        get => _storagePath.SanitizeDirectory();
        set => _storagePath=value;
    }
    public ParsedChapter Chapter { get; set; } = new ParsedChapter();
    public List<string> Tags { get; set; } = [];
    public long? ChapterCount { get; set; }
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public DateTime? ComicUploadDateUTC { get; set; }
    public string? Type { get; set; }

    public List<Page> Pages { get; set; }= [];
    public bool IsUpdate { get; set; } = false;
}