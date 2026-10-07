using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using RensaioBackend.Migration.Models;
using RensaioBackend.Models;
using RensaioBackend.Models.Abstractions;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Utils;
using System.Runtime;
using System.Text.Json;

namespace RensaioBackend.Extensions
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
        public static DownloadChapterInfo? ToDownloadChapterInfo(this EnqueueEntity e)
        {
            if (string.IsNullOrEmpty(e.JobParameters))
                return null;
            ChapterDownload? ch = JsonSerializer.Deserialize<ChapterDownload>(e.JobParameters);
            if (ch == null)
                return null;
            return new DownloadChapterInfo
            {
                ChapterNumber = ch.Chapter?.ParsedNumber,
                DownloadDateUTC = e.FinishedDate,
                Status = e.Status,
                Chapter = ch
            };
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
                InstanceId = series.Id,
                DisplayName = series.Sources?.Any(s => s.IsTitle) == true ? null : series.Title,
                StartChapter = series.StartFromChapter,
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

        public static string? CategoryFromPath(string path, SettingsDto settings)
        {
            var categories = settings?.Categories ?? [];
            if (categories.Length == 0)
                return null;
            path = path.Replace('\\', '/');
            foreach (var category in categories)
            {
                string b1 = category + "/";
                if (path.StartsWith(b1, true, System.Globalization.CultureInfo.InvariantCulture))
                    return category;
            }
            return null;
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
                ReleaseCadenceDays = s.ReleaseCadenceDays.HasValue
                    ? (int?)Math.Abs(s.ReleaseCadenceDays.Value)
                    : null,
                Path = EnvironmentSetup.IsDocker ? s.StoragePath : Path.Combine(settings.StorageFolder, s.StoragePath),
                IsActive = s.Sources.Any(a => !a.IsDisabled && !a.IsUninstalled),
                HasUnknown = s.Sources.Any(a => a.IsUnknown),
                StoragePath = s.StoragePath,
                ChapterCount = s.ChapterCount,
                ChapterList = s.Sources
                    .SelectMany(a => a.Chapters)
                    .Where(c => !c.IsDeleted && !string.IsNullOrEmpty(c.Filename))
                    .Select(c => c.Number).Distinct()
                    .FormatDecimalRanges(),
                Providers = new List<ProviderExtendedDto>(),
                Category = CategoryFromPath(s.StoragePath, settings)
            };
            SmallProviderDto? lastChangeProvider = null;
            DateTime dt = DateTime.MinValue;

            if (s.Sources != null && s.Sources.Count > 0)
            {
                // Sort providers: permanent (IsStorage) first, then temporary (normal Mihon-linked), then local/unknown
                var sortedSources = s.Sources
                    .OrderBy(p => p.IsUnknown || p.IsDisabled || p.IsUninstalled || p.IsLocal ? 2 :
                                  p.IsStorage ? 0 : 1)
                    .ToList();
                foreach (var provider in sortedSources)
                {
                    var providerDto = new ProviderExtendedDto
                    {
                        Id = provider.Id,
                        Provider = provider.Provider,
                        Scanlator = provider.Scanlator,
                        Lang = provider.Language,
                        Title = provider.Title,
                        Url = provider.Url,
                        ThumbnailUrl = string.IsNullOrEmpty(provider.ThumbnailUrl) ? s.ThumbnailUrl : provider.ThumbnailUrl,
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

        /// <summary>
        /// Builds the unified, series-level chapter list by merging every source's chapter rows by
        /// number. For each chapter it reports whether a file is on disk (and which source holds it,
        /// preferring the storage source) versus genuinely missing, plus the remote-capable sources
        /// that know the chapter (for the re-download picker). DB-only — no provider network call;
        /// "missing" reflects chapters Rensaiō knows about that are not on disk, not a live upstream
        /// diff. Intentionally-skipped chapters (below a provider's cutoff, ShouldDownload == false
        /// and never downloaded) are omitted so they don't inflate the missing count.
        /// </summary>
        public static List<ChapterDetailDto> ToChapterDetailList(this SeriesEntity s)
        {
            ArgumentNullException.ThrowIfNull(s);
            List<SeriesProviderEntity> sources = s.Sources?.ToList() ?? new List<SeriesProviderEntity>();

            // Sources we can actually fetch from — drives the re-download picker.
            List<SeriesProviderEntity> remoteCapable = sources
                .Where(p => !p.IsUnknown && !p.IsLocal && !p.IsDisabled && !p.IsUninstalled
                    && !string.IsNullOrEmpty(p.MihonProviderId))
                .ToList();

            var result = new List<ChapterDetailDto>();
            var groups = sources
                .SelectMany(p => p.Chapters
                    .Where(c => !c.IsDeleted && c.Number != null)
                    .Select(c => (Provider: p, Chapter: c)))
                .GroupBy(t => t.Chapter.Number);

            foreach (var g in groups)
            {
                var rows = g.ToList();
                var downloadedRows = rows.Where(r => !string.IsNullOrEmpty(r.Chapter.Filename)).ToList();
                bool downloaded = downloadedRows.Count > 0;

                // A not-downloaded chapter only counts as "missing" if a source still wants it.
                // Rows below a provider's cutoff carry ShouldDownload == false and are skipped.
                bool wanted = rows.Any(r => r.Chapter.ShouldDownload);
                if (!downloaded && !wanted)
                    continue;

                // Holder of the on-disk file: prefer the storage source.
                var holder = downloadedRows
                    .OrderBy(r => r.Provider.IsStorage ? 0 : 1)
                    .FirstOrDefault();

                string name = rows
                    .Select(r => r.Chapter.Name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;

                List<ChapterSourceDto> available = remoteCapable
                    .Where(p => p.Chapters.Any(c => !c.IsDeleted && c.Number == g.Key))
                    .Select(p => new ChapterSourceDto { Id = p.Id, Name = p.Provider })
                    .ToList();

                result.Add(new ChapterDetailDto
                {
                    Number = g.Key,
                    Name = name,
                    Downloaded = downloaded,
                    SourceProviderId = holder.Provider?.Id,
                    SourceProviderName = holder.Provider?.Provider,
                    AvailableProviders = available
                });
            }

            return result.OrderByDescending(c => c.Number).ToList();
        }
    }


}

