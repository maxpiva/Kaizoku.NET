namespace KaizokuBackend.Models.Database;

public class Chapter
{
    public string? Name { get; set; } = string.Empty;
    public decimal? Number { get; set; }
    public DateTime? ProviderUploadDate { get; set; }
    public string? Url { get; set; }
    public int ProviderIndex { get; set; }
    public DateTime? DownloadDate { get; set; }
    public bool ShouldDownload { get; set; }
    public bool IsDeleted { get; set; }
    public int? PageCount { get; set; }
    public string? Filename { get; set; }
}