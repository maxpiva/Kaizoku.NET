using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Utils;
using System.Runtime;
using System.Text.Json;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for model conversion and formatting
    /// </summary>
    public static class ModelExtensions
    {
        /// <summary>
        /// Converts an Enqueue entity to a QueueState object
        /// </summary>
        /// <param name="enqueue">The Enqueue entity</param>
        /// <returns>QueueState object</returns>
        public static QueueState ToJobState(this Enqueue enqueue)
        {
            return new QueueState
            {
                Id = enqueue.Id.ToString(),
                Key = enqueue.Key,
                Parameters = enqueue.JobParameters,
                JobType = enqueue.JobType,
                Status = enqueue.Status,
                EnqueuedDate = enqueue.EnqueuedDate,
                FinishedDate = enqueue.FinishedDate,
                ScheduledDate = enqueue.ScheduledDate,
                StartedDate = enqueue.StartedDate,
                Queue = enqueue.Queue,
                RetryCount = enqueue.RetryCount,
                Priority = enqueue.Priority
            };
        }
        public static LatestSeriesInfo ToSeriesInfo(this LatestSerie serie, ContextProvider provider)
        {
            return new LatestSeriesInfo
            {
                Id = serie.SuwayomiId.ToString(),
                Provider = serie.Provider,
                Status = serie.Status,
                Title = serie.Title,
                ThumbnailUrl = $"{provider.BaseUrl}{serie.ThumbnailUrl}",
                Language = serie.Language,
                ChapterCount = serie.ChapterCount,
                FetchDate = serie.FetchDate,
                Url = serie.Url,
                Artist = serie.Artist,
                Author = serie.Author,
                Description = serie.Description,
                Genre = serie.Genre,
                LatestChapter = serie.LatestChapter,
                LatestChapterTitle = serie.LatestChapterTitle,
                SuwayomiSourceId = serie.SuwayomiSourceId,
                InLibrary = serie.InLibrary,
                SeriesId = serie.SeriesId
            };
        }
        public static DownloadInfo? ToDownloadInfo(this Enqueue e)
        {
            if (string.IsNullOrEmpty(e.JobParameters))
                return null;
            ChapterDownload? ch = JsonSerializer.Deserialize<ChapterDownload>(e.JobParameters);
            if (ch == null)
                return null;
            return new DownloadInfo
            {
                Id = e.Id,
                Chapter = ch.Chapter.ChapterNumber,
                ChapterTitle = ch.Chapter.Name,
                Scanlator = ch.Scanlator,
                Provider = ch.ProviderName,
                Status = e.Status,
                DownloadDateUTC = e.FinishedDate,
                ScheduledDateUTC = e.ScheduledDate,
                Language = ch.Language,
                Retries = e.RetryCount,
                Title = ch.Title,
                Url = ch.Url,
                ThumbnailUrl = ch.ThumbnailUrl,
            };
        }
        public static int GetLocalGroupMax(this Dictionary<string, int> counts, string group, int max)
        {
            if (!counts.TryGetValue(group, out int value))
                return max;
            int count = max - value;
            if (count < 0)
                return 0;
            return count;
        }
        public static KaizokuInfo ToKaizokuInfo(this Series series)
        {
            ArgumentNullException.ThrowIfNull(series);
            var info = new KaizokuInfo
            {
                Title = series.Title,
                Status = series.Status,
                Artist = series.Artist,
                Author = series.Author,
                Description = series.Description,
                Genre = series.Genre?.ToList() ?? new List<string>(),
                Type = series.Type ?? string.Empty,
                ChapterCount = series.ChapterCount,
                IdDisabled = false,
                Path = series.StoragePath
            };
            if (series.Sources != null && series.Sources.Count != 0)
            {
                info.Providers = series.Sources
                    .Select(sp => new ProviderInfo
                    {
                        Provider = sp.Provider,
                        Language = sp.Language,
                        Scanlator = sp.Scanlator,
                        Title = sp.Title,
                        ThumbnailUrl = sp.ThumbnailUrl,
                        Status = sp.Status,
                        IsStorage = sp.IsStorage,
                        IsDisabled = sp.IsDisabled,
                        ChapterCount = (int)sp.Chapters.Count(c => !c.IsDeleted),
                        ChapterList = sp.Chapters
                            .Where(c => !c.IsDeleted)
                            .Select(c => c.Number)
                            .DecimalRanges()
                            .Select(r => new StartStop
                            {
                                Start = r.From,
                                End = r.To
                            }).ToList(),
                        Archives = sp.Chapters
                            .Where(c => !c.IsDeleted)
                            .Select(a => new ArchiveInfo
                            {
                                ArchiveName = a.Filename ?? "",
                                ChapterNumber = a.Number,
                                Index = a.ProviderIndex,
                                CreationDate = a.DownloadDate ?? a.ProviderUploadDate
                            }).ToList()
                    })
                    .ToList();
            }
            else
            {
                info.Providers = new List<ProviderInfo>();
            }
            info.LastUpdatedUTC = series.Sources?.Max(a => a.FetchDate);
            return info;
        }
        public static Chapter ToChapter(this SuwayomiChapter chapter)
        {
            return new Chapter
            {
                Name = chapter.Name,
                Number = chapter.ChapterNumber,
                ProviderUploadDate = DateTimeOffset.FromUnixTimeMilliseconds(chapter.UploadDate).UtcDateTime,
                Url = chapter.RealUrl,
                ProviderIndex = chapter.Index,
                PageCount = chapter.PageCount,
            };
        }
       
        public static SeriesExtendedInfo ToSeriesExtendedInfo(this Series s, ContextProvider cp, Settings settings)
        {
            var info = new SeriesExtendedInfo
            {
                Id = s.Id,
                Title = s.Title,
                Description = s.Description,
                ThumbnailUrl = cp.BaseUrl + s.ThumbnailUrl,
                Artist = s.Artist,
                PauseDownloads = s.PauseDownloads,
                Author = s.Author,
                Genre = s.Genre?.ToDistinctPascalCase() ?? new List<string>(),
                Status = s.Status,
                Type = s.Type,
                Path = EnvironmentSetup.IsDocker ? s.StoragePath : Path.Combine(settings.StorageFolder, s.StoragePath),
                IsActive = s.Sources.Any(a => !a.IsDisabled && !a.IsUninstalled && !a.IsUnknown),
                StoragePath = s.StoragePath,
                ChapterCount = s.ChapterCount,
                ChapterList = s.Sources
                    .SelectMany(a => a.Chapters)
                    .Where(c => !c.IsDeleted && !string.IsNullOrEmpty(c.Filename))
                    .Select(c => c.Number).Distinct()
                    .FormatDecimalRanges(),
                Providers = new List<ProviderExtendedInfo>()
            };

            if (s.Sources != null && s.Sources.Count > 0)
            {
                foreach (var provider in s.Sources)
                {
                    var providerInfo = new ProviderExtendedInfo
                    {
                        Id = provider.Id,
                        Provider = provider.Provider,
                        Scanlator = provider.Scanlator,
                        Lang = provider.Language,
                        Title = provider.Title,
                        Url = provider.Url,
                        ThumbnailUrl = cp.BaseUrl + (string.IsNullOrEmpty(provider.ThumbnailUrl) ? "serie/thumb/unknown" : provider.ThumbnailUrl),
                        Artist = provider.Artist ?? "",
                        Author = provider.Author ?? "",
                        Description = provider.Description ?? "",
                        Genre = provider.Genre?.ToList() ?? new List<string>(),
                        Status = provider.Status,
                        ChapterCount = provider.ChapterCount ?? 0,
                        IsStorage = provider.IsStorage,
                        UseTitle = provider.IsTitle,
                        UseCover = provider.IsCover,
                        IsDisabled = provider.IsDisabled,
                        IsUninstalled = provider.IsUninstalled,
                        IsUnknown = provider.IsUnknown,
                        LastChangeUTC = provider.FetchDate ?? DateTime.MinValue,
                        LastChapter = provider.Chapters.MaxNull(c => c.Number),
                        LastUpdatedUTC = provider.FetchDate ?? DateTime.MinValue,
                        ContinueAfterChapter = provider.ContinueAfterChapter,
                        ChapterList = provider.Chapters.Select(c => c.Number).FormatDecimalRanges() ?? "",
                    };
                    info.Providers.Add(providerInfo);

                }
                Chapter? last = s.Sources.SelectMany(a => a.Chapters)
                    .Where(c => !c.IsDeleted && !string.IsNullOrEmpty(c.Filename))
                    .OrderByDescending(a => a.Number).FirstOrDefault();
                if (last != null)
                {
                    info.LastChapter = last.Number;
                    info.LastChangeUTC = last.ProviderUploadDate ?? DateTime.MinValue;
                }
                var lastProvider = s.Sources
                    .Where(a => !a.IsDisabled && a.FetchDate.HasValue)
                    .OrderByDescending(a => a.FetchDate!.Value)
                    .FirstOrDefault();

                if (lastProvider != null)
                {
                    info.LastChangeProvider = new SmallProviderInfo
                    {
                        Provider = lastProvider.Provider,
                        Scanlator = lastProvider.Scanlator,
                        Language = lastProvider.Language,
                        Url = lastProvider.Url,
                        IsStorage = lastProvider.IsStorage,
                    };
                }

            }

            return info;
        }
    }


}