using KaizokuBackend.Models;

namespace KaizokuBackend.Models.Dto;

public class DownloadCardInfoDto : DownloadSummaryBase
{
    public int PageCount { get; set; }
    public decimal? ChapterNumber { get; set; }
    public string ChapterName { get; set; } = string.Empty;
}