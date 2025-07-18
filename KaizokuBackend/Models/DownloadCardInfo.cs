namespace KaizokuBackend.Models;

public class DownloadCardInfo
{
    public int PageCount { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Language { get; set; } = "";
    public string? Scanlator { get; set; }
    public string Title { get; set; } = "";
    public decimal? ChapterNumber { get; set; }
    public string ChapterName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; } = null;
}