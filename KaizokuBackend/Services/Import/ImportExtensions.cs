using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Import.Models;
using KaizokuBackend.Services.Helpers;
using System.Collections.Generic;
using System.Linq;
using Action = KaizokuBackend.Models.Database.Action;

namespace KaizokuBackend.Services.Import;

public static class ImportExtensions
{
    public static List<LinkedSeries> FindAndLinkSimilarSeries(this List<SuwayomiSeries> series, ContextProvider cp, double threshold = 0.1)
    {
        // ...moved logic from ModelExtensions...
        if (series == null || series.Count == 0)
        {
            return new List<LinkedSeries>();
        }
        var seriesGroups = new Dictionary<string, List<SuwayomiSeries>>();
        foreach (var s in series)
        {
            if (string.IsNullOrWhiteSpace(s.Title))
            {
                continue;
            }
            var normalizedTitle = s.Title.NormalizeTitle();
            if (seriesGroups.TryGetValue(normalizedTitle, out List<SuwayomiSeries>? value))
            {
                value.Add(s);
            }
            else
            {
                seriesGroups[normalizedTitle] = new List<SuwayomiSeries> { s };
            }
        }
        var linkedSeries = new List<LinkedSeries>();
        foreach (var group in seriesGroups.Values)
        {
            if (group.Count == 1)
            {
                var seris = group[0];
                linkedSeries.Add(new LinkedSeries
                {
                    Id = seris.Id.ToString(),
                    ProviderId = seris.SourceId,
                    Title = seris.Title,
                    ThumbnailUrl = cp.RewriteSeriesThumbnail(seris),
                    LinkedIds = new List<string> { seris.Id.ToString() }
                });
            }
            else
            {
                var allIds = group.Select(s => s.Id.ToString()).ToList();
                foreach (var s in group)
                {
                    linkedSeries.Add(new LinkedSeries
                    {
                        Id = s.Id.ToString(),
                        ProviderId = s.SourceId,
                        Title = s.Title,
                        ThumbnailUrl = cp.RewriteSeriesThumbnail(s),
                        LinkedIds = allIds
                    });
                }
            }
        }
        linkedSeries.MergeSimilarSeries(threshold);
        return linkedSeries;
    }

    public static void MergeSimilarSeries(this List<LinkedSeries> linkedSeries, double threshold = 0.1)
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
                    string id1 = series1.Id.ToString();
                    string id2 = series2.Id.ToString();
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
            string seriesId = series.Id.ToString();
            if (similarityGroups.TryGetValue(seriesId, out var group))
            {
                series.LinkedIds = group.ToList();
            }
        }
        var idToSeriesMap = linkedSeries.ToDictionary(s => s.Id.ToString(), s => s);
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
            series.LinkedIds.Remove(series.Id.ToString());
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
    public static KaizokuInfo? ToKaizokuInfo(this List<NewDetectedChapter> chapters)
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
        var result = new KaizokuInfo
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
            ProviderInfo pinfo = new ProviderInfo();
            pinfo.Title = chaps.FirstOrDefault(a => !string.IsNullOrEmpty(a.Title))?.Title ?? "";
            pinfo.Provider = p.prov;
            pinfo.Language = p.lan;
            pinfo.Scanlator = chaps.FirstOrDefault(a => !string.IsNullOrEmpty(a.Scanlator))?.Scanlator ?? "";

            List<decimal?> cnumbs = chaps.Select(a => a.Chapter).OrderBy(a => a).ToList();
            List<(decimal from, decimal to)> res = cnumbs.DecimalRanges();
            pinfo.ChapterList = res.Select(a => new StartStop { Start = a.from, End = a.to }).ToList();

            List<ArchiveInfo> archives = chaps.Select(a => new ArchiveInfo
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
    public static (bool change, KaizokuInfo kz) Merge(this KaizokuInfo original, KaizokuInfo scanned)
    {
        bool ch = false;
        List<ProviderInfo> newProvs = new List<ProviderInfo>();
        List<ProviderInfo> accepted = new List<ProviderInfo>();

        foreach (ProviderInfo p in scanned.Providers)
        {
            ProviderInfo? org = original.Providers.FirstOrDefault(a => a.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) && a.Language.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase) && a.Scanlator.Equals(p.Scanlator, StringComparison.InvariantCultureIgnoreCase));
            if (org==null)
                org = original.Providers.FirstOrDefault(a => a.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) && a.Language.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase));
            if (org == null)
                org = original.Providers.FirstOrDefault(a => a.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase));

            if (org == null)
            {
                newProvs.Add(p);
                ch = true;
            }
            else
            {
                org.ChapterList=p.ChapterList;
                org.ChapterCount = p.ChapterCount;
                org.Archives = p.Archives;
                org.IsDisabled = p.IsDisabled;
                org.Language = p.Language;
                org.Provider = p.Provider;
                org.Scanlator = p.Scanlator;
                org.Title = !string.IsNullOrEmpty(p.Title) ? p.Title : org.Title;
                org.Scanlator = !string.IsNullOrEmpty(p.Scanlator) ? p.Scanlator : org.Scanlator;
                ch = true;
            }
        }

        original.Providers.AddRange(newProvs);
        return (ch, original);
    }
}