using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Import.Models;
using KaizokuBackend.Services.Helpers;
using System.Collections.Generic;
using System.Linq;
using Action = KaizokuBackend.Models.Database.Action;

namespace KaizokuBackend.Services.Series;

public static class SeriesExtensions
{
    public static LatestSerie PopulateSeries(this LatestSerie l, SuwayomiSource source, SuwayomiSeries fs)
    {
        l.Artist = fs.Artist;
        l.Provider = source.Name;
        l.Language = source.Lang;
        l.Status = fs.Status;
        l.Title = fs.Title;
        l.ThumbnailUrl = fs.RewriteToKaizokuPath();
        l.ChapterCount = fs.ChapterCount;
        l.Author = fs.Author;
        l.Description = fs.Description;
        l.Genre = fs.Genre;
        l.SuwayomiId = fs.Id;
        l.Url = fs.RealUrl;
        l.SuwayomiSourceId = fs.SourceId;
        return l;
    }

    public static void ApplyImportInfo(this KaizokuBackend.Models.Database.Import import, ImportInfo info)
    {
        import.Status = info.Status;
        import.Action = info.Action;
        import.ContinueAfterChapter = info.ContinueAfterChapter;
        if (import.Series == null || import.Series.Count == 0)
            return;
        if (info.Series != null)
        {
            foreach (SmallSeries s in info.Series)
            {
                FullSeries? fs = import.Series.FirstOrDefault(a =>
                    a.Id == s.Id &&
                    a.Title == s.Title &&
                    a.Provider == s.Provider &&
                    a.Lang == s.Lang &&
                    a.Scanlator == s.Scanlator);
                if (fs != null)
                {
                    fs.IsStorage = s.IsStorage;
                    fs.UseTitle = s.UseTitle;
                    fs.UseCover = s.UseCover;
                    fs.IsSelected = s.Preferred;
                    fs.ContinueAfterChapter = info.ContinueAfterChapter;
                }
            }
        }
    }

    public static ImportInfo ToImportInfo(this KaizokuBackend.Models.Database.Import import, string baseUrl)
    {
        decimal? lastRecordChap = import.Info.Providers.Max((ProviderInfo a) => a.Archives.Max((ArchiveInfo c) => c.ChapterNumber));
        var importInfo = new ImportInfo
        {
            Path = import.Info.Path,
            Title = import.Info.Title,
            Status = import.Status,
            Action = import.Action,
            ContinueAfterChapter = lastRecordChap ?? -1,
            Series = new List<SmallSeries>()
        };
        if (import.Series == null || import.Series.Count == 0)
            return importInfo;
        foreach (var fs in import.Series)
        {
            if (string.IsNullOrEmpty(fs.ThumbnailUrl))
                fs.ThumbnailUrl = baseUrl + "serie/thumb/unknown";
            else if (!fs.ThumbnailUrl.StartsWith("http"))
                fs.ThumbnailUrl = baseUrl + fs.ThumbnailUrl;
            var smallSeries = new SmallSeries
            {
                Id = fs.Id,
                ProviderId = fs.ProviderId,
                Provider = fs.Provider,
                Lang = fs.Lang,
                ThumbnailUrl = fs.ThumbnailUrl,
                Title = fs.Title,
                ChapterCount = fs.ChapterCount,
                Url = fs.Url,
                Scanlator = fs.Scanlator,
                LastChapter = fs.Chapters.MaxNull(c => c.Number),
                ChapterList = fs.Chapters.Count != 0 ? fs.Chapters.Select(a => a.Number).FormatDecimalRanges() : string.Empty,
                IsStorage = fs.IsStorage,
                UseCover = fs.UseCover,
                UseTitle = fs.UseTitle,
                Preferred = fs.IsSelected
            };
            importInfo.Series.Add(smallSeries);
        }
        SetPreferredSeries(importInfo.Series, import.Info.Providers);
        EnsurePreferredStorageSeries(importInfo.Series);
        decimal? from = importInfo.ContinueAfterChapter;
        decimal? to = importInfo.Series.Max(a => a.LastChapter);
        if (from.HasValue && to.HasValue)
        {
            AddCompletition(importInfo.Series, import.Series, from.Value, to.Value);
        }
        if (import.Series.Any(a => a.Status == SeriesStatus.COMPLETED || a.Status == SeriesStatus.PUBLISHING_FINISHED))
        {
            decimal? chap = import.Series.Max((FullSeries a) => a.Chapters.Max((Chapter b) => b.Number));
            if (chap <= importInfo.ContinueAfterChapter && importInfo.Action != Action.Skip)
            {
                importInfo.Status = ImportStatus.Completed;
                importInfo.Action = Action.Add;
            }
            if (import.Series.Where(a => a.ExistingProvider).Any(a => a.Chapters.Max(b => b.Number) < chap))
            {
                if (importInfo.Action != Action.Skip)
                {
                    importInfo.Status = ImportStatus.DoNotChange;
                    importInfo.Action = Action.Skip;
                }
            }
        }
        SmallSeries? series = importInfo.Series.FirstOrDefault(a => a.Preferred && a.IsStorage);
        if (series == null)
        {
            series = importInfo.Series.FirstOrDefault(a => a.Preferred);
            if (series != null)
                series.IsStorage = true;
        }
        if (series != null)
        {
            series.UseTitle = true;
            series.UseCover = true;
        }
        return importInfo;
    }

    public static void SetPreferredSeries(List<SmallSeries> seriesList, List<ProviderInfo> providers)
    {
        if (seriesList == null || seriesList.Count == 0)
            return;
        if (seriesList.Any(a => a.Preferred))
            return;
        var exactMatches = new List<SmallSeries>();
        foreach (var series in seriesList)
        {
            var matchingProvider = providers.FirstOrDefault(p =>
                p.Provider.Equals(series.Provider, StringComparison.OrdinalIgnoreCase) &&
                p.Language.Equals(series.Lang, StringComparison.OrdinalIgnoreCase));
            if (matchingProvider != null && !matchingProvider.IsDisabled)
            {
                exactMatches.Add(series);
            }
        }
        if (exactMatches.Count != 0)
        {
            var preferredSeries = exactMatches
                .OrderByDescending(s => s.ChapterCount)
                .First();
            preferredSeries.Preferred = true;
            return;
        }
        var maxChapterSeries = seriesList
            .OrderByDescending(s => s.LastChapter)
            .ToList();
        if (maxChapterSeries.Count > 0)
        {
            decimal? maxLastChapter = maxChapterSeries[0].LastChapter;
            var seriesWithMaxChapters = maxChapterSeries
                .Where(s => s.LastChapter == maxLastChapter)
                .ToList();
            if (seriesWithMaxChapters.Count == 1)
            {
                seriesWithMaxChapters[0].Preferred = true;
            }
            else if (seriesWithMaxChapters.Count > 1)
            {
                var mostComplete = seriesWithMaxChapters.OrderByDescending(a => a.ChapterCount).First();
                mostComplete.Preferred = true;
            }
        }
    }

    public static void EnsurePreferredStorageSeries(List<SmallSeries> seriesList)
    {
        if (seriesList == null || seriesList.Count == 0)
            return;
        var preferred = seriesList.Where(s => s.Preferred).ToList();
        if (preferred.Count == 0)
            return;
        if (preferred.All(s => !s.IsStorage))
        {
            var storageSeries = seriesList.Where(s => s.IsStorage).OrderByDescending(s => s.ChapterCount).ToList();
            if (storageSeries.Count > 0)
            {
                var mostComplete = storageSeries.First();
                mostComplete.Preferred = true;
            }
        }
    }

    public static void AddCompletition(List<SmallSeries> seriesList, List<FullSeries> fullSeries, decimal FromChapter, decimal maxChapter)
    {
        Dictionary<SmallSeries, List<StartStop>> fStartStop = new Dictionary<SmallSeries, List<StartStop>>();
        foreach (SmallSeries s in seriesList)
        {
            FullSeries f = fullSeries.First(a => a.Id == s.Id && a.Provider == s.Provider && a.Lang == s.Lang && a.Scanlator == s.Scanlator);
            fStartStop[s] = f.Chapters.Select(a => a.Number).DecimalRanges()
                .Select(c => new StartStop { Start = c.From, End = c.To }).ToList();
        }
        List<List<StartStop>> ls = fStartStop.Where(a => a.Key.Preferred).Select(a => a.Value).ToList();
        Dictionary<SmallSeries, List<StartStop>> notPrefs = fStartStop.Where(a => !a.Key.Preferred).ToDictionary(a => a.Key, a => a.Value);
        List<StartStop> current = MergeAllRanges(ls);
        if (IsRangeCompleted(new StartStop { Start = FromChapter, End = maxChapter }, current))
            return;
        List<StartStop>? missing = GetMissingRanges(new StartStop { Start = FromChapter, End = maxChapter }, current);
        if (missing == null)
            return;
        if (notPrefs.Count == 0)
            return;
        Dictionary<StartStop, List<SmallSeries>> matches = new Dictionary<StartStop, List<SmallSeries>>();
        foreach (StartStop m in missing)
        {
            foreach (var k in notPrefs)
            {
                if (IsRangeCompleted(m, k.Value))
                {
                    if (!matches.ContainsKey(m))
                        matches[m] = new List<SmallSeries>();
                    matches[m].Add(k.Key);
                }
            }
        }
        if (matches.Count == 0)
            return;
        List<SmallSeries> final = MinimalCoveringSmallSeries(matches);
        final.ForEach(a => a.Preferred = true);
    }

    public static List<StartStop> MergeRanges(List<StartStop> a, List<StartStop>? b)
    {
        var all = new List<StartStop>();
        if (a != null) all.AddRange(a);
        if (b != null) all.AddRange(b);
        if (all.Count == 0) return new List<StartStop>();
        all = all.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var merged = new List<StartStop>();
        StartStop? current = null;
        foreach (var range in all)
        {
            if (current == null)
            {
                current = new StartStop { Start = range.Start, End = range.End };
            }
            else
            {
                if (range.Start <= current.End + 1)
                {
                    current.End = Math.Max(current.End, range.End);
                }
                else
                {
                    merged.Add(current);
                    current = new StartStop { Start = range.Start, End = range.End };
                }
            }
        }
        if (current != null)
            merged.Add(current);
        return merged;
    }

    public static bool IsRangeCompleted(StartStop target, List<StartStop> completedRanges)
    {
        if (target == null || completedRanges == null || completedRanges.Count == 0)
            return false;
        foreach (var range in completedRanges)
        {
            if (range.Start <= target.Start && range.End >= target.End)
                return true;
        }
        return false;
    }

    public static List<StartStop>? GetMissingRanges(StartStop target, List<StartStop> completedRanges)
    {
        if (completedRanges.Count == 0)
            return new List<StartStop> { new StartStop { Start = target.Start, End = target.End } };
        var merged = MergeRanges(completedRanges, null);
        decimal current = target.Start;
        var missing = new List<StartStop>();
        foreach (var range in merged)
        {
            if (range.End < current)
                continue;
            if (range.Start > target.End)
                break;
            if (range.Start > current)
            {
                missing.Add(new StartStop { Start = current, End = Math.Min(range.Start - 1, target.End) });
            }
            current = Math.Max(current, range.End + 1);
            if (current > target.End)
                break;
        }
        if (current <= target.End)
        {
            missing.Add(new StartStop { Start = current, End = target.End });
        }
        return missing.Count == 0 ? null : missing;
    }

    public static List<StartStop> MergeAllRanges(List<List<StartStop>> rangesList)
    {
        var all = new List<StartStop>();
        if (rangesList.Count == 0)
            return all;
        foreach (var sublist in rangesList)
        {
            all.AddRange(sublist);
        }
        return MergeRanges(all, null);
    }

    public static List<SmallSeries> MinimalCoveringSmallSeries(Dictionary<StartStop, List<SmallSeries>> keyToSeries)
    {
        var seriesToKeys = new Dictionary<SmallSeries, HashSet<StartStop>>();
        foreach (var kvp in keyToSeries)
        {
            StartStop key = kvp.Key;
            foreach (var s in kvp.Value)
            {
                if (!seriesToKeys.TryGetValue(s, out var set))
                {
                    set = new HashSet<StartStop>();
                    seriesToKeys[s] = set;
                }
                set.Add(key);
            }
        }
        var uncovered = new HashSet<StartStop>(keyToSeries.Keys);
        var result = new List<SmallSeries>();
        while (uncovered.Count > 0)
        {
            SmallSeries? best = null;
            int bestCover = -1;
            foreach (var kvp in seriesToKeys)
            {
                int cover = kvp.Value.Count(k => uncovered.Contains(k));
                if (cover > bestCover)
                {
                    best = kvp.Key;
                    bestCover = cover;
                }
            }
            if (best == null || bestCover == 0)
                break;
            result.Add(best);
            foreach (var k in seriesToKeys[best])
                uncovered.Remove(k);
            seriesToKeys.Remove(best);
        }
        return result;
    }


    public static SeriesInfo ToSeriesInfo(this Models.Database.Series series, ContextProvider cp)
    {
        var info = new SeriesInfo
        {
            Id = series.Id,
            Title = series.Title,
            Description = series.Description,
            ThumbnailUrl = cp.BaseUrl + series.ThumbnailUrl,
            Artist = series.Artist,
            Author = series.Author,
            Genre = series.Genre?.ToDistinctPascalCase() ?? new List<string>(),
            Status = series.Status,
            Type = series.Type,
            IsActive = series.Sources.Any(a => !a.IsDisabled && !a.IsUninstalled && !a.IsUnknown),
            PausedDownloads = series.PauseDownloads,
            ChapterCount = series.ChapterCount,
            StoragePath = series.StoragePath
        };

        if (series.Sources != null && series.Sources.Count > 0)
        {
            SmallProviderInfo? lastChangeProvider = null;
            DateTime dt = DateTime.MinValue;

            foreach (var provider in series.Sources.Where(a => !a.IsDisabled))
            {
                SmallProviderInfo sm = new SmallProviderInfo();
                sm.Provider = provider.Provider;
                sm.Scanlator = provider.Scanlator;
                sm.Language = provider.Language;
                sm.IsStorage = provider.IsStorage;
                sm.Url = provider.Url;
                DateTime? last = provider.Chapters.Where(a => !string.IsNullOrEmpty(a.Filename))
                    .MaxNull(c => c.ProviderUploadDate);
                if (last != null && last > dt)
                {
                    dt = last.Value;
                    lastChangeProvider = sm;
                }

                info.Providers.Add(sm);
            }

            info.LastChapter = series.Sources.Max((SeriesProvider a) => a.Chapters.Max((Chapter c) => c.Number));
            if (lastChangeProvider != null)
            {
                info.LastChangeProvider = lastChangeProvider;
                info.LastChangeUTC = dt;
            }
            else
            {
                info.LastChangeProvider = new SmallProviderInfo();
                info.LastChangeUTC = DateTime.MinValue;
            }
        }

        return info;
    }
    /// <summary>
    /// Creates a new Series entity from consolidated full series data
    /// </summary>
    /// <param name="consolidatedSeries">Consolidated full series data</param>
    /// <returns>New Series entity</returns>
    public static Models.Database.Series ToSeries(this FullSeries consolidatedSeries, string storagePath)
    {
        return new Models.Database.Series
        {
            Id = Guid.NewGuid(),
            Title = consolidatedSeries.Title,
            Description = consolidatedSeries.Description ?? string.Empty,
            ThumbnailUrl = consolidatedSeries.ThumbnailUrl ?? string.Empty,
            Artist = consolidatedSeries.Artist ?? string.Empty,
            Author = consolidatedSeries.Author ?? string.Empty,
            Genre = consolidatedSeries.Genre ?? new List<string>(),
            Status = consolidatedSeries.Status,
            StoragePath = storagePath,
            Type = consolidatedSeries.Type,
            ChapterCount = consolidatedSeries.ChapterCount,
            Sources = new List<SeriesProvider>()
        };
    }
    /// <summary>
    /// Consolidates data from multiple full series into a single FullSeries object
    /// </summary>
    /// <param name="seriesList">List of full series with details from different sources</param>
    /// <returns>A consolidated FullSeries object</returns>
    public static FullSeries ConsolidateSeriesData(this List<FullSeries> seriesList)
    {
        if (seriesList.Count == 0)
        {
            throw new ArgumentException("No series provided for consolidation");
        }

        // Start with the first series as a base
        var primarySeries = seriesList.First();

        // Find the series with most complete data (non-null fields) to use as primary
        foreach (var series in seriesList)
        {
            int primaryNonNullCount = CountNonNullFields(primarySeries);
            int currentNonNullCount = CountNonNullFields(series);

            if (currentNonNullCount > primaryNonNullCount)
            {
                primarySeries = series;
            }

            // If a cover is explicitly marked to be used, prioritize that
            if (series.UseCover)
            {
                primarySeries.ThumbnailUrl = series.ThumbnailUrl;
            }
        }

        // Consolidate genres from all sources
        var allGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in seriesList)
        {
            if (series.Genre != null)
            {
                foreach (var genre in series.Genre)
                {
                    allGenres.Add(genre);
                }
            }
        }

        primarySeries.Genre = allGenres.ToDistinctPascalCase();

        // Use the highest chapter count available
        primarySeries.ChapterCount = seriesList
            .Select(s => s.ChapterCount)
            .DefaultIfEmpty(0)
            .Max();

        // Prefer non-null descriptions if the primary one is null
        if (string.IsNullOrEmpty(primarySeries.Description))
        {
            primarySeries.Description = seriesList
                .FirstOrDefault(s => !string.IsNullOrEmpty(s.Description))
                ?.Description ?? string.Empty;
        }

        // Prefer non-null author/artist if the primary one is null
        if (string.IsNullOrEmpty(primarySeries.Author))
        {
            primarySeries.Author = seriesList
                .FirstOrDefault(s => !string.IsNullOrEmpty(s.Author))
                ?.Author ?? string.Empty;
        }

        if (string.IsNullOrEmpty(primarySeries.Artist))
        {
            primarySeries.Artist = seriesList
                .FirstOrDefault(s => !string.IsNullOrEmpty(s.Artist))
                ?.Artist ?? string.Empty;
        }

        return primarySeries;
    }

    /// <summary>
    /// Count the number of non-null fields in a FullSeries object
    /// </summary>
    /// <param name="series">The series to analyze</param>
    /// <returns>The number of non-null fields</returns>
    public static int CountNonNullFields(this FullSeries series)
    {
        int count = 0;

        if (!string.IsNullOrEmpty(series.Title)) count++;
        if (!string.IsNullOrEmpty(series.Artist)) count++;
        if (!string.IsNullOrEmpty(series.Author)) count++;
        if (!string.IsNullOrEmpty(series.Description)) count++;
        if (!string.IsNullOrEmpty(series.ThumbnailUrl)) count++;
        if (series.Genre != null && series.Genre.Count > 0) count += series.Genre.Count;
        if (!string.IsNullOrEmpty(series.Type)) count++;
        if (series.ChapterCount > 0) count++;

        return count;
    }


    public static decimal? CalculateContinueAfterChapter(this IEnumerable<SeriesProvider> providers)
    {
        decimal? continueAfterChapter = providers
            .SelectMany(a => a.Chapters.Where(b => !string.IsNullOrEmpty(b.Filename) && !b.IsDeleted))
            .Max(a => a.Number);
        decimal? maxStPossible = providers.Where(a => a.IsStorage).SelectMany(a => a.Chapters).Max(a => a.Number);
        foreach (SeriesProvider s in providers)
        {
            decimal? proposedMax = Decimal.MaxValue;

            decimal? sMax = s.Chapters.Max(a => a.Number);
            if (s.IsUnknown)
                proposedMax = sMax;
            else if (s.IsStorage)
                proposedMax = continueAfterChapter;
            else
            {
                if (sMax != null && maxStPossible != null)
                {
                    proposedMax = maxStPossible;
                }
                else if (sMax != null)
                {
                    proposedMax = sMax;
                }
            }

            if (proposedMax < s.ContinueAfterChapter || s.ContinueAfterChapter == null)
                s.ContinueAfterChapter = proposedMax;
            if (s.ContinueAfterChapter > sMax)
                s.ContinueAfterChapter = sMax;
        }

        return continueAfterChapter;
    }

    public static void AssignArchives(this SeriesProvider provider, List<ArchiveInfo>? archives)
    {
        if (provider.Chapters.Count > 0 && archives != null)
        {
            List<ArchiveInfo> processed = new List<ArchiveInfo>();
            List<ArchiveInfo> notProcessed = new List<ArchiveInfo>();
            //map existing chapters to archives
            foreach (ArchiveInfo arch in archives)
            {
                Chapter? c = provider.Chapters.FirstOrDefault(a => a.Number == arch.ChapterNumber);
                if (c != null)
                {
                    arch.Index = c.ProviderIndex;
                    processed.Add(arch);
                }
                else
                    notProcessed.Add(arch);
            }

            if (notProcessed.Count > 0 && processed.Count > 0)
            {
                processed.InterpolateChapterIndexes(notProcessed);
            }
        }

        if (archives != null)
        {
            List<Chapter> toBeDeletedChapters = provider.Chapters.ToList();
            foreach (ArchiveInfo a in archives)
            {
                Chapter? pc = provider.Chapters.FirstOrDefault(c => c.Number == a.ChapterNumber);

                if (pc != null)
                {
                    if (pc.ProviderUploadDate != null)
                        pc.ProviderUploadDate = a.CreationDate;
                    pc.DownloadDate = a.CreationDate;
                    pc.ShouldDownload = false;
                    pc.Filename = a.ArchiveName;
                    pc.Number = a.ChapterNumber;
                    pc.IsDeleted = false;
                    toBeDeletedChapters.Remove(pc);
                }
                else
                {
                    pc = new Chapter();
                    pc.ShouldDownload = false;
                    pc.ProviderIndex = a.Index;
                    pc.Number = a.ChapterNumber;
                    pc.Filename = a.ArchiveName;
                    pc.DownloadDate = pc.ProviderUploadDate = a.CreationDate;
                    pc.IsDeleted = false;
                    provider.Chapters.Add(pc);
                }
            }

            toBeDeletedChapters.ForEach(c => c.IsDeleted = true);
        }
    }


    public static SeriesProvider CreateOrUpdate(this FullSeries fs,
        SeriesProvider? provider = null)
    {
        if (provider == null)
        {
            provider = new SeriesProvider
            {
                Id = Guid.NewGuid(),
                Provider = fs.Provider,
                Language = fs.Lang,
                Scanlator = fs.Scanlator ?? string.Empty,
                Title = fs.Title,
                IsDisabled = false,
                Chapters = new List<Chapter>()
            };
        }


        provider.SuwayomiId = int.Parse(fs.Id);
        provider.Url = fs.Url;
        provider.ThumbnailUrl = fs.ThumbnailUrl.RemoveSchemaDomainFromUrl();
        provider.Artist = fs.Artist;
        provider.Author = fs.Author;
        provider.Description = fs.Description;
        provider.Genre = fs.Genre;
        provider.ChapterCount = fs.ChapterCount > 0 ? fs.ChapterCount : null;
        provider.ContinueAfterChapter = fs.ContinueAfterChapter;
        if (fs.ContinueAfterChapter == null && provider.ChapterCount > 0 && provider.IsUnknown)
            fs.ContinueAfterChapter = provider.Chapters.Max(a => a.Number);
        provider.IsStorage = fs.IsStorage;
        provider.Status = fs.Status;
        provider.FetchDate = fs.LastUpdatedUTC;
        provider.IsTitle = fs.UseTitle;
        provider.IsCover = fs.UseCover;
        foreach (Chapter c in fs.Chapters)
        {
            Chapter? pc = provider.Chapters.FirstOrDefault(a => a.Number == c.Number);
            if (pc != null)
            {
                if (pc.ProviderUploadDate != null && c.ProviderUploadDate != null &&
                    pc.ProviderUploadDate < c.ProviderUploadDate)
                {
                    //Chapter was updated, mark for download again
                    pc.ShouldDownload = true;
                }
            }
            else
            {
                c.ShouldDownload = fs.ContinueAfterChapter < c.Number || fs.ContinueAfterChapter == null;
                provider.Chapters.Add(c);
            }
        }

        return provider;
    }

    public static SeriesStatus BestStatus(IEnumerable<SeriesProvider> providers)
    {
        SeriesProvider? p = providers.Where(a => a.Status != SeriesStatus.UNKNOWN).FirstOrDefault();
        if (p == null)
            p = providers.FirstOrDefault();
        if (p == null)
            return SeriesStatus.UNKNOWN;
        return p.Status;
    }

    // Consolidates data from multiple SeriesProvider into a single FullSeries-like object
    public static FullSeries ToFullSeries(this IEnumerable<SeriesProvider> providers)
    {
        if (!providers.Any())
            throw new ArgumentException("No providers given for consolidation");

        // Find the provider with the most complete data
        SeriesProvider best = providers.First();
        int bestScore = best.CountNonNullFields();
        string? bestCover = best.ThumbnailUrl;
        foreach (var prov in providers)
        {
            int score = prov.CountNonNullFields();
            if (score > bestScore)
            {
                best = prov;
                bestScore = score;
            }

            if (prov.IsCover && !string.IsNullOrEmpty(prov.ThumbnailUrl))
                bestCover = prov.ThumbnailUrl;
        }

        // Consolidate genres
        var allGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prov in providers)
            if (prov.Genre != null)
                foreach (var genre in prov.Genre)
                    allGenres.Add(genre);

        // Build a FullSeries-like object (do not mutate providers)
        FullSeries fs = new FullSeries
        {
            Title = best.Title,
            Description = string.IsNullOrEmpty(best.Description)
                ? providers.FirstOrDefault(p => !string.IsNullOrEmpty(p.Description))?.Description ?? string.Empty
                : best.Description,
            ThumbnailUrl = bestCover,
            Artist = string.IsNullOrEmpty(best.Artist)
                ? providers.FirstOrDefault(p => !string.IsNullOrEmpty(p.Artist))?.Artist ?? string.Empty
                : best.Artist,
            Author = string.IsNullOrEmpty(best.Author)
                ? providers.FirstOrDefault(p => !string.IsNullOrEmpty(p.Author))?.Author ?? string.Empty
                : best.Author,
            Genre = allGenres.ToDistinctPascalCase(),
            ChapterCount = (int)providers.Select(p => p.ChapterCount ?? 0).DefaultIfEmpty(0).Max(),
            Status = BestStatus(providers)
        };
        string? nTitle = providers.FirstOrDefault(a => a.IsTitle)?.Title;
        string? nThumb = providers.FirstOrDefault(a => a.IsCover)?.ThumbnailUrl;
        if (nTitle != null)
            fs.Title = nTitle;
        if (nThumb != null)
            fs.ThumbnailUrl = nThumb;
        return fs;
    }


    // Helper: Count non-null fields in SeriesProvider
    public static int CountNonNullFields(this SeriesProvider provider)
    {
        int count = 0;
        if (!string.IsNullOrEmpty(provider.Title)) count++;
        if (!string.IsNullOrEmpty(provider.Artist)) count++;
        if (!string.IsNullOrEmpty(provider.Author)) count++;
        if (!string.IsNullOrEmpty(provider.Description)) count++;
        if (!string.IsNullOrEmpty(provider.ThumbnailUrl)) count++;
        if (provider.Genre != null && provider.Genre.Count > 0) count += provider.Genre.Count;
        if (provider.ChapterCount.HasValue && provider.ChapterCount.Value > 0) count++;
        return count;
    }

    /// <summary>
    /// Interpolates the Index property for each element in <paramref name="toInterpolate"/> based on the
    /// ChapterNumber and Index values in <paramref name="reference"/>.
    /// </summary>
    /// <param name="reference">An IEnumerable of IChapterIndex with valid Index values.</param>
    /// <param name="toInterpolate">An IEnumerable of IChapterIndex whose Index values will be set/interpolated.</param>
    public static void InterpolateChapterIndexes(this IEnumerable<IChapterIndex> reference,
        IEnumerable<IChapterIndex> toInterpolate)
    {
        var refList = reference
            .Where(r => r.ChapterNumber.HasValue)
            .OrderBy(r => r.ChapterNumber)
            .ToList();

        foreach (var item in toInterpolate)
        {
            if (!item.ChapterNumber.HasValue)
                continue;

            // Find the two reference points for interpolation
            var lower = refList.LastOrDefault(r => r.ChapterNumber <= item.ChapterNumber);
            var upper = refList.FirstOrDefault(r => r.ChapterNumber >= item.ChapterNumber);

            if (lower != null && upper != null && lower != upper)
            {
                // Linear interpolation
                var x0 = lower.ChapterNumber!.Value;
                var x1 = upper.ChapterNumber!.Value;
                var y0 = lower.Index;
                var y1 = upper.Index;
                var x = item.ChapterNumber!.Value;

                item.Index = (int)Math.Round(y0 + (y1 - y0) * ((double)(x - x0) / (double)(x1 - x0)));
            }
            else if (lower != null)
            {
                item.Index = lower.Index;
            }
            else if (upper != null)
            {
                item.Index = upper.Index;
            }
            // else: no reference, leave as is
        }
    }

    public static SeriesProvider ToSeriesProvider(this ProviderInfo providerInfo)
    {
        return new SeriesProvider
        {
            Id = Guid.NewGuid(),
            Provider = providerInfo.Provider,
            Language = providerInfo.Language,
            Scanlator = providerInfo.Scanlator ?? string.Empty,
            Title = providerInfo.Title,
            IsDisabled = false,
            Chapters = [],
            IsUnknown = providerInfo.Provider == "Unknown",
            SuwayomiId = 0,
            ThumbnailUrl = providerInfo.ThumbnailUrl,
            ChapterCount = providerInfo.ChapterCount > 0 ? providerInfo.ChapterCount : null,
            ContinueAfterChapter = providerInfo.Archives?.Max(a => a.ChapterNumber),
            IsStorage = providerInfo.IsStorage,
            Status = providerInfo.Status,
            FetchDate = DateTime.UtcNow,
            IsTitle = false,
            IsCover = false,
        };
    }


    public static string? DeriveTypeFromGenre(this List<string> genres, string[] folderCategories, bool partial = false)
    {
        if (genres == null || genres.Count == 0 || folderCategories == null || folderCategories.Length == 0)
        {
            return null;
        }

        // Normalize genres for comparison
        var normalizedGenres = genres.Select(g => g.NormalizeGenres()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check each folder category if it matches any genre
        foreach (var category in folderCategories)
        {
            var normalizedCategory = category.NormalizeGenres();

            // Direct match
            if (normalizedGenres.Contains(normalizedCategory))
            {
                return category; // Return the original (non-normalized) category name
            }

            if (partial)
            {
                // Check for partial matches (e.g., if a genre contains the category name)
                foreach (var genre in normalizedGenres)
                {
                    if (genre.Contains(normalizedCategory, StringComparison.OrdinalIgnoreCase) ||
                        normalizedCategory.Contains(genre, StringComparison.OrdinalIgnoreCase))
                    {
                        return category; // Return the original category name
                    }
                }
            }
        }

        return null; // No match found
    }

    public static void FillSeriesFromFullSeries(this Models.Database.Series dbSeries, FullSeries consolidatedSeries)
    {
        dbSeries.Title = consolidatedSeries.Title;
        dbSeries.Description = consolidatedSeries.Description ?? string.Empty;
        dbSeries.ThumbnailUrl = consolidatedSeries.ThumbnailUrl ?? string.Empty;
        dbSeries.Artist = consolidatedSeries.Artist ?? string.Empty;
        dbSeries.Author = consolidatedSeries.Author ?? string.Empty;
        dbSeries.Genre = consolidatedSeries.Genre ?? new List<string>();
        dbSeries.Type = consolidatedSeries.Type;
        if (consolidatedSeries.ChapterCount > dbSeries.ChapterCount)
        {
            dbSeries.ChapterCount = consolidatedSeries.ChapterCount;
        }
    }
}
