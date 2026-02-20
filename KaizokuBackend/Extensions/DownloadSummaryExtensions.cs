using KaizokuBackend.Models;
using KaizokuBackend.Models.Dto;

namespace KaizokuBackend.Extensions;

public static class DownloadSummaryExtensions
{
    public static DownloadSummary ToDownloadSummary(this ChapterDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);

        return new DownloadSummary
        {
            Title = download.Title,
            Provider = download.ProviderName,
            Scanlator = download.Scanlator,
            Language = download.Language,
            ChapterNumber = download.Chapter?.ParsedNumber,
            ChapterTitle = download.Chapter?.ParsedName,
            ChapterName = download.ChapterName,
            PageCount = download.PageCount,
            ThumbnailUrl = download.ThumbnailUrl,
            Url = download.Url
        };
    }

    public static DownloadInfoDto ToInfoDto(this DownloadSummary summary)
    {
        return new DownloadInfoDto
        {
            Id = summary.Id,
            Title = summary.Title,
            Provider = summary.Provider,
            Scanlator = summary.Scanlator,
            Language = summary.Language,
            Chapter = summary.ChapterNumber,
            ChapterTitle = summary.ChapterTitle,
            DownloadDateUTC = summary.DownloadDateUTC,
            Status = summary.Status,
            ScheduledDateUTC = summary.ScheduledDateUTC,
            Retries = summary.Retries,
            ThumbnailUrl = summary.ThumbnailUrl,
            Url = summary.Url
        };
    }

    public static DownloadCardInfoDto ToCardInfoDto(this DownloadSummary summary)
    {
        return new DownloadCardInfoDto
        {
            PageCount = summary.PageCount,
            Provider = summary.Provider,
            Language = summary.Language,
            Scanlator = summary.Scanlator,
            Title = summary.Title,
            ChapterNumber = summary.ChapterNumber,
            ChapterName = summary.ChapterName,
            ThumbnailUrl = summary.ThumbnailUrl
        };
    }
}
