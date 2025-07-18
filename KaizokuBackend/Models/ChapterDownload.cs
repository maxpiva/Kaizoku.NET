using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models;

public class ChapterDownload
{
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public Guid SeriesProviderId { get; set; }
    public int SuwayomiId { get; set; }
    public int SuwayomiIndex { get; set; }
    public int PageCount { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ChapterName { get; set; } = string.Empty;
    public string? Scanlator { get; set; }
    public string Title { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    public string Language { get; set; } = "";
    public string? ThumbnailUrl { get; set; } = null;

    public string? Url { get; set; } = null;
    public int Retries { get; set; }

    private string _storagePath = string.Empty;
    public string StoragePath
    {
        get => _storagePath.SanitizeDirectory();
        set => _storagePath=value;
    }
    public SuwayomiChapter Chapter { get; set; } = new SuwayomiChapter();
    public List<string> Tags { get; set; } = [];
    public long? ChapterCount { get; set; }
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public DateTime? ComicUploadDateUTC { get; set; }
    public string? Type { get; set; }
    public bool ChapterLoaded { get; set; } = false;

    public bool IsUpdate { get; set; } = false;
}