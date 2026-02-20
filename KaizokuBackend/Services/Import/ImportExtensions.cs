using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Services.Import.Models;
using KaizokuBackend.Services.Helpers;
using System.Collections.Generic;
using System.Linq;
using Action = KaizokuBackend.Models.Action;
using Mihon.ExtensionsBridge.Models.Extensions;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Import;

public static class ImportExtensions
{
    public static List<LinkedSeriesDto> FindAndLinkSimilarSeries(this List<(ParsedManga Manga, string MihonProviderId, string Language)> series, double threshold = 0.1)
    {
        // ...moved logic from ModelExtensions...
        if (series == null || series.Count == 0)
        {
            return new List<LinkedSeriesDto>();
        }
        var seriesGroups = new Dictionary<string, List<(ParsedManga Manga, string MihonProviderId, string Language)>>();
        foreach (var s in series)
        {
            if (string.IsNullOrWhiteSpace(s.Manga.Title))
            {
                continue;
            }
            var normalizedTitle = s.Manga.Title.NormalizeTitle();
            if (seriesGroups.TryGetValue(normalizedTitle, out List<(ParsedManga Manga, string MihonProviderId, string Language)>? value))
            {
                value.Add(s);
            }
            else
            {
                seriesGroups[normalizedTitle] = new List<(ParsedManga Manga, string MihonProviderId, string Language)> { s };
            }
        }
        var linkedSeries = new List<LinkedSeriesDto>();
        foreach (var group in seriesGroups.Values)
        {
            if (group.Count == 1)
            {
                var seris = group[0];
                string id = seris.MihonProviderId + "|" + seris.Manga.Url;
                LinkedSeriesDto ls = new LinkedSeriesDto
                {
                    MihonId = id,
                    MihonProviderId = seris.MihonProviderId,
                    Lang = seris.Language == "all" ? string.Empty : seris.Language,
                    Title = seris.Manga.Title,
                    ThumbnailUrl = seris.Manga.ThumbnailUrl,
                    LinkedIds = new List<string> { id }
                };
                seris.Manga.FillBridgeItemInfo(ls);
                linkedSeries.Add(ls);
            }
            else
            {
                var allIds = group.Select(s => s.MihonProviderId + "|"+s.Manga.Url).ToList();
                foreach (var s in group)
                {
                    string id = s.MihonProviderId + "|" + s.Manga.Url;
                    LinkedSeriesDto ls = new LinkedSeriesDto
                    {
                        MihonId = id,
                        MihonProviderId = s.MihonProviderId,
                        Lang = s.Language == "all" ? string.Empty : s.Language,
                        Title = s.Manga.Title,
                        ThumbnailUrl = s.Manga.ThumbnailUrl,
                        LinkedIds = allIds
                    };
                    s.Manga.FillBridgeItemInfo(ls);
                    linkedSeries.Add(ls);
                }
            }
        }
        linkedSeries.MergeSimilarSeries(threshold);
        return linkedSeries;
    }


    public static void MergeSimilarSeries(this List<LinkedSeriesDto> linkedSeries, double threshold = 0.1)
    {
        if (linkedSeries.Count <= 1)
        {
            return;
        }
        var similarityGroups = new Dictionary<string, HashSet<string>>();
        for (int i = 0; i < linkedSeries.Count; i++)
        {
            for (int j = i + 1; j < linkedSeries.Count; j++)
            {
                var series1 = linkedSeries[i];
                var series2 = linkedSeries[j];
                if (series1.LinkedIds.Any(id => series2.LinkedIds.Contains(id)))
                {
                    continue;
                }
                if (series1.Title.AreStringSimilar(series2.Title, threshold))
                {
                    string id1 = series1.MihonId!;
                    string id2 = series2.MihonId!;
                    if (!similarityGroups.TryGetValue(id1, out var group1))
                    {
                        group1 = new HashSet<string>(series1.LinkedIds);
                        similarityGroups[id1] = group1;
                    }
                    if (!similarityGroups.TryGetValue(id2, out var group2))
                    {
                        group2 = new HashSet<string>(series2.LinkedIds);
                        similarityGroups[id2] = group2;
                    }
                    foreach (var id in group2)
                    {
                        group1.Add(id);
                    }
                    foreach (var id in group1)
                    {
                        group2.Add(id);
                    }
                }
            }
        }
        foreach (var series in linkedSeries)
        {
            string seriesId = series.MihonId!;
            if (similarityGroups.TryGetValue(seriesId, out var group))
            {
                series.LinkedIds = group.ToList();
            }
        }
        var idToSeriesMap = linkedSeries.ToDictionary(s => s.MihonId!, s => s);
        foreach (var series in linkedSeries)
        {
            var consolidatedLinks = new HashSet<string>(series.LinkedIds);
            foreach (var linkedId in series.LinkedIds.ToList())
            {
                if (idToSeriesMap.TryGetValue(linkedId, out var linkedSeries2))
                {
                    foreach (var transitiveId in linkedSeries2.LinkedIds)
                    {
                        consolidatedLinks.Add(transitiveId);
                    }
                }
            }
            series.LinkedIds = consolidatedLinks.ToList();
            series.LinkedIds.Remove(series.MihonId!);
        }
    }

    public static void FillMissingChapterNumbers(this IEnumerable<IChapterIndex> chapters)
    {
        if (chapters == null || !chapters.Any())
            return;
        var ordered = chapters.OrderBy(c => c.Index).ToList();
        if (ordered.All(c => c.ChapterNumber == null))
        {
            foreach (var c in ordered)
                c.ChapterNumber = c.Index + 1;
            return;
        }
        int n = ordered.Count;
        int i = 0;
        while (i < n)
        {
            if (ordered[i].ChapterNumber != null)
            {
                i++;
                continue;
            }
            int prev = i - 1;
            while (prev >= 0 && ordered[prev].ChapterNumber == null)
                prev--;
            int next = i + 1;
            while (next < n && ordered[next].ChapterNumber == null)
                next++;
            if (prev >= 0 && next < n)
            {
                var prevNum = ordered[prev].ChapterNumber!.Value;
                var nextNum = ordered[next].ChapterNumber!.Value;
                int prevIdx = ordered[prev].Index;
                int nextIdx = ordered[next].Index;
                int gap = nextIdx - prevIdx;
                decimal step = (nextNum - prevNum) / gap;
                for (int j = prev + 1; j < next; j++)
                {
                    ordered[j].ChapterNumber = prevNum + step * (ordered[j].Index - prevIdx);
                }
                i = next;
            }
            else if (prev >= 0)
            {
                var prevNum = ordered[prev].ChapterNumber!.Value;
                int prevIdx = ordered[prev].Index;
                for (int j = prev + 1; j < n && ordered[j].ChapterNumber == null; j++)
                {
                    ordered[j].ChapterNumber = prevNum + (ordered[j].Index - prevIdx);
                }
                break;
            }
            else if (next < n)
            {
                var nextNum = ordered[next].ChapterNumber!.Value;
                int nextIdx = ordered[next].Index;
                for (int j = next - 1; j >= 0 && ordered[j].ChapterNumber == null; j--)
                {
                    ordered[j].ChapterNumber = nextNum - (nextIdx - ordered[j].Index);
                }
                i = next + 1;
            }
            else
            {
                break;
            }
        }
    }
    public static void FillMissingChapterNumbers(this IEnumerable<ParsedChapter> chapters)
    {
        if (chapters == null || !chapters.Any())
            return;
        var ordered = chapters.OrderBy(c => c.Index).ToList();
        if (ordered.All(c => c.ParsedNumber == -1m))
        {
            foreach (var c in ordered)
                c.ParsedNumber = c.Index + 1;
            return;
        }
        int n = ordered.Count;
        int i = 0;
        while (i < n)
        {
            if (ordered[i].ParsedNumber != -1m)
            {
                i++;
                continue;
            }
            int prev = i - 1;
            while (prev >= 0 && ordered[prev].ParsedNumber == -1m)
                prev--;
            int next = i + 1;
            while (next < n && ordered[next].ParsedNumber == -1m)
                next++;
            if (prev >= 0 && next < n)
            {
                var prevNum = ordered[prev].ParsedNumber;
                var nextNum = ordered[next].ParsedNumber;
                int prevIdx = ordered[prev].Index;
                int nextIdx = ordered[next].Index;
                int gap = nextIdx - prevIdx;
                decimal step = (nextNum - prevNum) / gap;
                for (int j = prev + 1; j < next; j++)
                {
                    ordered[j].ParsedNumber = prevNum + step * (ordered[j].Index - prevIdx);
                }
                i = next;
            }
            else if (prev >= 0)
            {
                var prevNum = ordered[prev].ParsedNumber;
                int prevIdx = ordered[prev].Index;
                for (int j = prev + 1; j < n && ordered[j].ParsedNumber == -1m; j++)
                {
                    ordered[j].ParsedNumber = prevNum + (ordered[j].Index - prevIdx);
                }
                break;
            }
            else if (next < n)
            {
                var nextNum = ordered[next].ParsedNumber;
                int nextIdx = ordered[next].Index;
                for (int j = next - 1; j >= 0 && ordered[j].ParsedNumber == -1m; j--)
                {
                    ordered[j].ParsedNumber = nextNum - (nextIdx - ordered[j].Index);
                }
                i = next + 1;
            }
            else
            {
                break;
            }
        }
    }
    public static ImportSeriesSnapshot? ToImportSeriesSnapshot(this List<NewDetectedChapter> chapters)
    {
        if (chapters.Count == 0)
        {
            return null;
        }

        var titleGroups = chapters
            .Where(c => !string.IsNullOrEmpty(c.Title))
            .GroupBy(c => c.Title)
            .OrderByDescending(g => g.Count())
            .ToList();

        string title = titleGroups.Count != 0 ? titleGroups.First().Key : "Unknown";
        var result = new ImportSeriesSnapshot
        {
            Title = title,
            Providers = [],
            KaizokuVersion = 1
        };

        var kaizokuMatches = chapters.Where(c => c.IsKaizokuMatch).ToList();
        var nonKaizokuMatches = chapters.Where(c => !c.IsKaizokuMatch).ToList();
        var providers = kaizokuMatches.GroupBy(a => (a.Provider, a.Language)).ToDictionary(a => a.Key, a => a.ToList());

        if (nonKaizokuMatches.Count > 0)
        {
            providers.Add(("Unknown", "en"), nonKaizokuMatches);
        }

        foreach ((string prov, string lan) p in providers.Keys)
        {
            List<NewDetectedChapter> chaps = providers[p];
            ImportProviderSnapshot pinfo = new ImportProviderSnapshot();
            pinfo.Title = chaps.FirstOrDefault(a => !string.IsNullOrEmpty(a.Title))?.Title ?? "";
            pinfo.Provider = p.prov;
            pinfo.Language = p.lan;
            pinfo.Scanlator = chaps.FirstOrDefault(a => !string.IsNullOrEmpty(a.Scanlator))?.Scanlator ?? "";

            List<decimal?> cnumbs = chaps.Select(a => a.Chapter).OrderBy(a => a).ToList();
            List<(decimal from, decimal to)> res = cnumbs.DecimalRanges();
            pinfo.ChapterList = res.Select(a => new StartStop { Start = a.from, End = a.to }).ToList();

            List<ProviderArchiveSnapshot> archives = chaps.Select(a => new ProviderArchiveSnapshot
            {
                ArchiveName = Path.GetFileName(a.Filename),
                ChapterNumber = a.Chapter,
                CreationDate = a.CreationDate
            }).ToList();

            archives = archives.OrderByChapter(a => (a.ChapterNumber?.ToString() ?? "")).ToList();
            int start = 0;
            archives.ForEach(a =>
            {
                a.Index = start;
                start++;
            });

            archives.FillMissingChapterNumbers();
            pinfo.Archives = archives;
            pinfo.ChapterCount = cnumbs.Count;
            result.Providers.Add(pinfo);
        }

        return result;
    }
    public static (bool change, ImportSeriesSnapshot kz) Merge(this ImportSeriesSnapshot original, ImportSeriesSnapshot scanned)
    {
        bool changed = original.Series.MergeProvidersFrom(scanned.Series);
        return (changed, original);
    }
}

