using KaizokuBackend.Models;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Downloads;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Utils;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Runtime;
using System.Text.Json;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for model conversion and formatting
    /// </summary>
    public static class ModelExtensions
    {

        public static LatestSeriesDto ToSeriesInfo(this LatestSerieEntity serie)
        {
            return new LatestSeriesDto
            {
                MihonId = serie.MihonId,
                MihonProviderId = serie.MihonProviderId,
                Provider = serie.Provider,
                Status = serie.Status,
                Title = serie.Title,
                ThumbnailUrl =serie.ThumbnailUrl,
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
                InLibrary = serie.InLibrary,
                SeriesId = serie.SeriesId
            };
        }
        public static DownloadSummary? ToDownloadSummary(this EnqueueEntity e)
        {
            if (string.IsNullOrEmpty(e.JobParameters))
                return null;

            ChapterDownload? ch = JsonSerializer.Deserialize<ChapterDownload>(e.JobParameters);
            if (ch == null)
                return null;

            DownloadSummary summary = ch.ToDownloadSummary();
            summary.Id = e.Id;
            summary.Status = e.Status;
            summary.DownloadDateUTC = e.FinishedDate;
            summary.ScheduledDateUTC = e.ScheduledDate;
            summary.Retries = e.RetryCount;
            return summary;
        }

        public static DownloadInfoDto? ToDownloadInfo(this EnqueueEntity e)
        {
            return e.ToDownloadSummary()?.ToInfoDto();
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
        public static ImportSeriesSnapshot ToImportSeriesSnapshot(this SeriesEntity series)
        {
            ArgumentNullException.ThrowIfNull(series);
            var info = new ImportSeriesSnapshot
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
                    .Select(sp => new ImportProviderSnapshot
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
                            .Select(a => new ProviderArchiveSnapshot
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
                info.Providers = new List<ImportProviderSnapshot>();
            }
            info.LastUpdatedUTC = series.Sources?.Max(a => a.FetchDate);
            return info;
        }
        public static Models.Chapter ToChapter(this ParsedChapter chapter)
        {
            return new Models.Chapter
            {
                Name = chapter.ParsedName,
                Number = chapter.ParsedNumber,
                ProviderUploadDate = chapter.DateUpload.DateTime,
                Url = chapter.RealUrl,
                ProviderIndex = chapter.Index
            };
        }
        public static ExtensionSourceDto ToExtensionSource(this TachiyomiSource source)
        {
            return new ExtensionSourceDto
            {
                Name = source.Name,
                Language = source.Language,
            };
        }
        public static ExtensionEntryDto ToExtensionEntry(this RepositoryEntry e, string repoName, string repoId)
        {
            return new ExtensionEntryDto
            {
                Name = e.Name,
                OnlineRepositoryName = repoName,
                OnlineRepositoryId = repoId,
                IsLocal = e.IsLocal,
                Package = e.Extension.Package,
                Version = e.Extension.Version,
                DownloadUTC = e.DownloadUTC,
                Id = e.Id.ToString(),
                Nsfw = e.Extension.Nsfw == 1,
                Sources = e.Extension.Sources.Select(s => s.ToExtensionSource()).ToList()
            };
        }
        public static ExtensionEntryDto ToExtensionEntry(this TachiyomiExtension e, string repoName, string repoId)
        {
            return new ExtensionEntryDto
            {
                Name = e.Name,
                OnlineRepositoryName = repoName,
                OnlineRepositoryId = repoId,
                IsLocal = false,
                Package = e.Package,
                Version = e.Version,
                Nsfw = e.Nsfw == 1,
                Sources = e.Sources.Select(s => s.ToExtensionSource()).ToList()
            };
        }
        /*
        public static Models.ExtensionView ToExtensionInfo(this ProviderWithExtension p)
        {
            return new Models.ExtensionView
            {
                Package = p.Provider.SourcePackageName,
                Name = p.Provider.Name,
                ThumbnailUrl = p.Provider.ThumbnailUrl,
                IsStorage = p.Provider.IsStorage,
                IsEnabled = p.Provider.IsEnabled,
                IsBroken = p.Provider.IsBroken,
                IsDead = p.Provider.IsDead,
                IsInstaled = true,
                ActiveEntry = p.RepoGroup?.ActiveEntry ?? 0,
                AutoUpdate = p.RepoGroup?.AutoUpdate ?? true,
                Entries = p.RepoGroup?.Entries.Select(e => e.ToExtensionEntry(p.Provider.RepositoryName)).ToList() ?? [],
            };
        }*/
        public static ExtensionDto ToExtensionInfo(this TachiyomiExtension ext)
        {
            return new ExtensionDto
            {
                Package = ext.Package,
                Name = ext.ParsedName(),
                IsStorage = true,
                IsEnabled = false,
                IsBroken = false,
                IsDead = false,
                IsInstaled = false,
                ActiveEntry = 0,
                AutoUpdate = false,
            }; 
        }
        public static void FillProviderStorage(this ProviderStorageEntity storage, RepositoryEntry entry, TachiyomiExtension extension, TachiyomiSource source, ISourceInterop? interop, string repoName, string repoId)
        {
            storage.Name = source.Name;
            storage.Language = source.Language;
            storage.SourceRepositoryId = repoId;
            storage.SourceRepositoryName = repoName;
            storage.SourcePackageName = extension.Package;
            storage.SourceSourceId = source.Id;
            storage.ThumbnailUrl = "ext://" + Path.Combine(entry.GetRelativeVersionFolder(), entry.Icon.FileName);
            storage.SupportLatest = interop?.SupportsLatest ?? false;
            storage.IsNSFW = extension.Nsfw == 1;
            storage.IsDead = false;
            storage.IsEnabled = true;
            storage.IsBroken = false;
        }
        public static Manga? ToManga(this IBridgeItemInfo info)
        {
            if (string.IsNullOrEmpty(info.BridgeItemInfo))
                return null;
            return JsonSerializer.Deserialize<Manga>(info.BridgeItemInfo);
        }
        public static void FillBridgeItemInfo(this Manga manga, IBridgeItemInfo item)
        {
            if (manga == null)
                return;
            item.BridgeItemInfo = JsonSerializer.Serialize<Manga>(manga);
        }

        public static List<string> GetGenres(this Manga m)
        {
            return m.Genre?.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? [];
        }
        public static SeriesExtendedDto ToSeriesExtendedInfo(this SeriesEntity s, SettingsDto settings)
        {
            var info = new SeriesExtendedDto
            {
                Id = s.Id,
                Title = s.Title,
                Description = s.Description,
                ThumbnailUrl = s.ThumbnailUrl,
                Artist = s.Artist,
                PausedDownloads = s.PauseDownloads,
                Author = s.Author,
                Genre = s.Genre?.ToDistinctPascalCase() ?? new List<string>(),
                Status = s.Status,
                Type = s.Type,
                StartFromChapter =  s.StartFromChapter,
                Path = EnvironmentSetup.IsDocker ? s.StoragePath : Path.Combine(settings.StorageFolder, s.StoragePath),
                IsActive = s.Sources.Any(a => !a.IsDisabled && !a.IsUninstalled),
                HasUnknown = s.Sources.Any(a=>!a.IsUnknown),
                StoragePath = s.StoragePath,
                ChapterCount = s.ChapterCount,
                ChapterList = s.Sources
                    .SelectMany(a => a.Chapters)
                    .Where(c => !c.IsDeleted && !string.IsNullOrEmpty(c.Filename))
                    .Select(c => c.Number).Distinct()
                    .FormatDecimalRanges(),
                Providers = new List<ProviderExtendedDto>()
            };
            SmallProviderDto? lastChangeProvider = null;
            DateTime dt = DateTime.MinValue;

            if (s.Sources != null && s.Sources.Count > 0)
            {
                foreach (var provider in s.Sources)
                {
                    var providerDto = new ProviderExtendedDto
                    {
                        Id = provider.Id,
                        Provider = provider.Provider,
                        Scanlator = provider.Scanlator,
                        Lang = provider.Language,
                        Title = provider.Title,
                        Url = provider.Url,
                        ThumbnailUrl = provider.ThumbnailUrl,
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
                        LastChangeUTC = provider.Chapters.MaxNull(a => a.DownloadDate) ?? provider.FetchDate ?? DateTime.MinValue,
                        LastChapter = provider.Chapters.MaxNull(c => c.Number),
                        LastUpdatedUTC = provider.Chapters.MaxNull(a=>a.ProviderUploadDate) ?? provider.FetchDate ?? DateTime.MinValue,
                        ContinueAfterChapter = provider.ContinueAfterChapter,
                        ChapterList = provider.Chapters.Select(c => c.Number).FormatDecimalRanges() ?? "",
                    };
                    //Previously matched, but search failed.
                    if (string.IsNullOrWhiteSpace(provider.BridgeItemInfo))
                        providerDto.IsUnknown = true;
                    info.Providers.Add(providerDto);

                    SmallProviderDto sm = provider.ToSmallProviderDto();
                    DateTime? lastm = provider.Chapters.Where(a => !string.IsNullOrEmpty(a.Filename))
                        .MaxNull(c => c.DownloadDate);
                    if (lastm != null && lastm > dt)
                    {
                        dt = lastm.Value;
                        lastChangeProvider = sm;
                    }

                }
                info.LastChapter = s.Sources.Max((SeriesProviderEntity a) => a.Chapters.Max((Models.Chapter c) => c.Number));
                if (lastChangeProvider != null)
                {
                    info.LastChangeProvider = lastChangeProvider;
                    info.LastChangeUTC = dt;
                }
                else
                {
                    info.LastChangeProvider = new SmallProviderDto();
                    info.LastChangeUTC = DateTime.MinValue;
                }
            }

            return info;
        }
    }


}

