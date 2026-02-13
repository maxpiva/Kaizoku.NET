using System;
using System.Collections.Generic;
using System.Linq;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Extensions;

/// <summary>
/// Provides shared helpers for computing import chapter/provider metrics.
/// </summary>
public static class ImportMetricsExtensions
{
    public static ImportChapterMetrics CalculateSeriesMetrics(this ImportEntity import)
    {
        ArgumentNullException.ThrowIfNull(import);
        return CalculateSeriesMetrics(import.Series, import.ContinueAfterChapter);
    }

    public static ImportChapterMetrics CalculateSeriesMetrics(IEnumerable<ProviderSeriesDetails>? series, decimal? continueAfterChapter)
    {
        if (series == null)
        {
            return ImportChapterMetrics.Empty;
        }

        HashSet<string> providers = new(StringComparer.InvariantCultureIgnoreCase);
        HashSet<decimal> chapterNumbers = new();

        foreach (ProviderSeriesDetails providerSeries in series.Where(s => s.IsSelected))
        {
            if (!string.IsNullOrWhiteSpace(providerSeries.Provider))
            {
                providers.Add(providerSeries.Provider);
            }

            foreach (decimal? number in providerSeries.Chapters.Select(c => c.Number))
            {
                if (!number.HasValue)
                {
                    continue;
                }

                if (continueAfterChapter.HasValue && number.Value <= continueAfterChapter.Value)
                {
                    continue;
                }

                chapterNumbers.Add(number.Value);
            }
        }

        return new ImportChapterMetrics(chapterNumbers.Count, providers.ToList());
    }

    public static ArchiveCompare CompareArchives(IEnumerable<ProviderArchiveSnapshot>? archives, IEnumerable<Chapter>? existingChapters)
    {
        if (archives == null || existingChapters == null)
        {
            return ArchiveCompare.Equal;
        }

        HashSet<string> archiveNames = archives
            .Where(a => !string.IsNullOrEmpty(a.ArchiveName))
            .Select(a => a.ArchiveName!.Trim())
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        HashSet<string> existingNames = existingChapters
            .Where(c => !string.IsNullOrEmpty(c.Filename))
            .Select(c => c.Filename!.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        bool missingInDb = archiveNames.Except(existingNames).Any();
        bool missingInArchives = existingNames.Except(archiveNames).Any();

        if (!missingInDb && !missingInArchives)
        {
            return ArchiveCompare.Equal;
        }

        ArchiveCompare result = 0;
        if (missingInDb)
        {
            result |= ArchiveCompare.MissingDB;
        }
        if (missingInArchives)
        {
            result |= ArchiveCompare.MissingArchive;
        }
        return result;
    }
}
