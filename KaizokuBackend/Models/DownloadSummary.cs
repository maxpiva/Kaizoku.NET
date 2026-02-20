using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models;

/// <summary>
/// Unified download projection shared across queue/status DTOs.
/// </summary>
public class DownloadSummary : DownloadSummaryBase
{
    public Guid Id { get; set; }
    public decimal? ChapterNumber { get; set; }
    public string? ChapterTitle { get; set; }
    public DateTime? DownloadDateUTC { get; set; }
    public QueueStatus Status { get; set; }
    public DateTime ScheduledDateUTC { get; set; }
    public int Retries { get; set; }
    public int PageCount { get; set; }
    public string ChapterName { get; set; } = string.Empty;
}
