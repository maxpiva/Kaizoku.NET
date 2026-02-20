namespace KaizokuBackend.Models;

public class Chapter : ChapterDescriptorBase
{
    public string? Name { get; set; } = string.Empty;
    public decimal? Number
    {
        get => ChapterNumber;
        set => ChapterNumber = value;
    }
    public DateTime? ProviderUploadDate { get; set; }
    public string? Url { get; set; }
    public int ProviderIndex { get; set; }
    public DateTime? DownloadDate { get; set; }
    public bool ShouldDownload { get; set; }
    public bool IsDeleted { get; set; }
    public int? PageCount { get; set; }
    public string? Filename { get; set; }
}