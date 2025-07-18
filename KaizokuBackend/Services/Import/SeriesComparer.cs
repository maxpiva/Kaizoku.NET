using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;

namespace KaizokuBackend.Services.Import;

public class SeriesComparer
{
    public List<KaizokuBackend.Models.Database.Series> FindMatchingSeries(IEnumerable<KaizokuBackend.Models.Database.Series> allSeries, KaizokuInfo kaizokuInfo)
    {
        var result = new List<KaizokuBackend.Models.Database.Series>();
        if (kaizokuInfo == null || allSeries == null)
            return result;

        // 1. Try to find a direct path match
        foreach (var series in allSeries)
        {
            if (!string.IsNullOrEmpty(series.StoragePath) &&
                !string.IsNullOrEmpty(kaizokuInfo.Path) &&
                string.Equals(series.StoragePath, kaizokuInfo.Path, StringComparison.InvariantCultureIgnoreCase))
            {
                result.Add(series);
                return result;
            }
        }

        // 2. If no path match, search for title similarity in all Sources
        foreach (var series in allSeries)
        {
            // Assume Series has a navigation property: ICollection<SeriesProvider> SeriesProviders
            if (series is null || series.Sources is null)
                continue;

            foreach (var provider in series.Sources)
            {
                if (!string.IsNullOrEmpty(provider.Title) &&
                    !string.IsNullOrEmpty(kaizokuInfo.Title) &&
                    provider.Title.AreStringSimilar(kaizokuInfo.Title, 0))
                {
                    result.Add(series);
                    break; // Only add each series once
                }
            }
        }

        return result;
    }


    public ArchiveCompare CompareArchives(KaizokuInfo kaizokuInfo, KaizokuBackend.Models.Database.Series series)
    {
        if (kaizokuInfo == null || series == null || kaizokuInfo.Providers == null || series.Sources == null)
            return 0;

        // Collect all archive names from KaizokuInfo (full paths, case-insensitive)
        var kaizokuArchives = kaizokuInfo.Providers
            .Where(p => p.Archives != null)
            .SelectMany(p => p.Archives)
            .Select(a => a.ArchiveName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        // Collect all file names from Series->Sources->Chapters (case-insensitive)
        var seriesArchives = series.Sources?
            .Where(sp => sp.Chapters != null)
            .SelectMany(sp => sp.Chapters)
            .Select(c => c.Filename)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n?.Trim())
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase) ?? new HashSet<string?>(StringComparer.InvariantCultureIgnoreCase);

        bool missingInDb = kaizokuArchives.Except(seriesArchives).Any();
        bool missingInArchives = seriesArchives.Except(kaizokuArchives).Any();

        ArchiveCompare result = 0;
        if (!missingInDb && !missingInArchives)
            result = ArchiveCompare.Equal;
        else
        {
            if (missingInDb)
                result |= ArchiveCompare.MissingDB;
            if (missingInArchives)
                result |= ArchiveCompare.MissingArchive;
        }
        return result;
    }
}