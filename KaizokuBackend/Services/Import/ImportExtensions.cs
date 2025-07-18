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