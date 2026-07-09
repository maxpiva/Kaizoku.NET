using RensaioBackend.Data;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Downloads;
using Microsoft.EntityFrameworkCore;

namespace RensaioBackend.Services.Series;

/// <summary>
/// Service responsible for computing and storing release cadence for series.
/// Uses download history to deduce whether a series releases weekly, biweekly, or monthly.
/// </summary>
public class CadenceCalculationService
{
    private readonly DownloadQueryService _downloadQuery;
    private readonly AppDbContext _db;
    private readonly ILogger<CadenceCalculationService> _logger;

    // Standard cadence mappings in days
    public const int CadenceWeekly = 7;
    public const int CadenceHalfMonth = 15;
    public const int CadenceMonthly = 30;

    // Minimum distinct chapter dates needed to compute cadence
    private const int MinDistinctDates = 4;

    // Thresholds for cadence mapping
    private const double WeeklyUpperBound = 10.0;
    private const double HalfMonthUpperBound = 22.0;

    public CadenceCalculationService(
        DownloadQueryService downloadQuery,
        AppDbContext db,
        ILogger<CadenceCalculationService> logger)
    {
        _downloadQuery = downloadQuery;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Computes and stores the release cadence for a series based on download history.
    /// Returns the computed cadence in days, or null if undetermined.
    /// </summary>
    public async Task<int?> RecalculateCadenceAsync(Guid seriesId, CancellationToken token = default)
    {
        string? name = null;
        try
        {
            // Step 0: Check if the user has manually set the cadence (indicated by negative value).
            // If so, skip auto-recalculation to preserve the user's preference.
            var existingSeries = await _db.Series
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId, token)
                .ConfigureAwait(false);

            name = existingSeries?.Title ?? seriesId.ToString();
            if (existingSeries?.ReleaseCadenceDays.HasValue == true && existingSeries.ReleaseCadenceDays.Value < 0)
            {
                _logger.LogDebug(
                    "Series {Name} has user-defined cadence ({Cadence} days), skipping auto-recalculation",
                    name,  Math.Abs(existingSeries.ReleaseCadenceDays.Value));
                return Math.Abs(existingSeries.ReleaseCadenceDays.Value);
            }

            // Step 1: Get download history from queue (primary source)
            List<DateTime> rawDates;

            var downloads = await _downloadQuery.GetDownloadsChapterInfoForSeriesAsync(seriesId, token)
                .ConfigureAwait(false);

            if (downloads.Count > 0)
            {
                // Extract effective dates (ComicUploadDateUTC > DownloadDateUTC)
                rawDates = ExtractEffectiveDates(downloads);
            }
            else
            {
                rawDates = [];
            }

            // Step 2: If queue data is insufficient, fall back to provider chapter data
            // (which persists even after queue cleanup deletes old enqueue records)
            if (rawDates.Count < MinDistinctDates)
            {
                _logger.LogDebug(
                    "Queue data insufficient ({Count} dates) for series {Name}, trying provider chapter data",
                    rawDates.Count, name);

                var providerDates = await ExtractEffectiveDatesFromProvidersAsync(seriesId, token)
                    .ConfigureAwait(false);

                if (providerDates.Count >= MinDistinctDates)
                {
                    _logger.LogDebug(
                        "Using {Count} dates from provider chapter data for series {Name}",
                        providerDates.Count, name);
                    rawDates = providerDates;
                }
                else
                {
                    _logger.LogDebug(
                        "Only {Count} dates from queue and {ProviderCount} from providers for series {Name}, need at least {Min}",
                        rawDates.Count, providerDates.Count, name, MinDistinctDates);
                    return await FallbackCadenceAsync(seriesId, name, token).ConfigureAwait(false);
                }
            }

            // Step 3: Filter and deduplicate
            var filteredDates = FilterAndDeduplicateDates(rawDates);
            if (filteredDates.Count < MinDistinctDates)
            {
                _logger.LogDebug("After filtering, only {Count} dates remain for series {Name}",
                    filteredDates.Count, name);
                return await FallbackCadenceAsync(seriesId, name, token).ConfigureAwait(false);
            }

            // Step 4: Calculate intervals between consecutive dates (chronological order)
            var intervals = CalculateIntervals(filteredDates);
            if (intervals.Count < 2)
            {
                _logger.LogDebug("Only {Count} intervals for series {Name}, need at least 2",
                    intervals.Count, name);
                return await FallbackCadenceAsync(seriesId, name, token).ConfigureAwait(false);
            }

            // Step 5: Remove outlier intervals (double releases, skipped releases)
            var cleanedIntervals = RemoveOutlierIntervals(intervals);
            if (cleanedIntervals.Count < 1)
            {
                _logger.LogDebug("After outlier removal, no intervals remain for series {Name}", name);
                return await FallbackCadenceAsync(seriesId, name, token).ConfigureAwait(false);
            }

            // Step 6: Compute median interval and map to standard cadence
            double medianInterval = ComputeMedian(cleanedIntervals);
            int? cadenceDays = MapIntervalToCadence(medianInterval);

            // Step 7: Store on the series entity
            if (cadenceDays.HasValue)
            {
                var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, token)
                    .ConfigureAwait(false);
                if (series != null)
                {
                    series.ReleaseCadenceDays = cadenceDays.Value;
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Computed cadence for series {title}: {CadenceDays} days (median interval: {MedianInterval:F1})",
                        series.Title, cadenceDays.Value, medianInterval);
                }
            }

            return cadenceDays;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing cadence for series {name}", name ?? seriesId.ToString());
            return null;
        }
    }

    /// <summary>
    /// Extracts effective dates from download history.
    /// Uses ComicUploadDateUTC if available, falls back to DownloadDateUTC.
    /// </summary>
    public List<DateTime> ExtractEffectiveDates(List<DownloadChapterInfo> downloads)
    {
        var dates = new List<DateTime>();

        foreach (var dl in downloads)
        {
            // Prefer ComicUploadDateUTC (provider upload date), fall back to download date
            DateTime? effectiveDate = dl.Chapter?.ComicUploadDateUTC ?? dl.DownloadDateUTC;

            if (effectiveDate.HasValue)
            {
                // Use date only (strip time) for day-level granularity
                dates.Add(effectiveDate.Value.Date);
            }
        }

        return dates;
    }

    /// <summary>
    /// Extracts effective dates from provider chapter records as a secondary data source.
    /// This is used when queue cleanup has purged old download records.
    /// Reads ProviderUploadDate and DownloadDate directly from each provider's chapter list.
    /// </summary>
    public async Task<List<DateTime>> ExtractEffectiveDatesFromProvidersAsync(Guid seriesId, CancellationToken token = default)
    {
        var dates = new List<DateTime>();

        var providers = await _db.SeriesProviders
            .Where(p => p.SeriesId == seriesId && !p.IsDisabled && !p.IsUninstalled)
            .AsNoTracking()
            .ToListAsync(token)
            .ConfigureAwait(false);

        foreach (var provider in providers)
        {
            foreach (var chapter in provider.Chapters)
            {
                // Prefer ProviderUploadDate (from the source), fall back to DownloadDate (when we downloaded it)
                DateTime? effectiveDate = chapter.ProviderUploadDate ?? chapter.DownloadDate;

                if (effectiveDate.HasValue)
                {
                    dates.Add(effectiveDate.Value.Date);
                }
            }
        }

        return dates;
    }

    /// <summary>
    /// Filters and deduplicates raw dates.
    /// Removes same-day duplicates and detects bulk initial uploads.
    /// </summary>
    public List<DateTime> FilterAndDeduplicateDates(List<DateTime> rawDates)
    {
        if (rawDates.Count == 0)
            return [];

        // Sort chronologically ascending
        var sorted = rawDates.OrderBy(d => d).ToList();

        // Deduplicate: keep only distinct dates
        var distinct = sorted.Distinct().ToList();

        // Detect bulk initial upload: if dates are all within a 3-day window from the first date,
        // consider it a bulk import and exclude them. There is no limit on how many dates
        // can be in the bulk window — could be 5 or 100 chapters uploaded at once.
        int bulkCutoff = 0;
        if (distinct.Count >= 2)
        {
            for (int i = 0; i < distinct.Count; i++)
            {
                if ((distinct[i] - distinct[0]).TotalDays <= 3)
                    bulkCutoff = i + 1;
                else
                    break;
            }
        }

        if (bulkCutoff > 0 && bulkCutoff < distinct.Count)
        {
            // Remove bulk dates, keep the rest
            return distinct.Skip(bulkCutoff).ToList();
        }

        return distinct;
    }

    /// <summary>
    /// Calculates day intervals between consecutive dates (chronological order).
    /// </summary>
    public List<double> CalculateIntervals(List<DateTime> sortedDates)
    {
        var intervals = new List<double>();
        for (int i = 1; i < sortedDates.Count; i++)
        {
            double days = (sortedDates[i] - sortedDates[i - 1]).TotalDays;
            if (days > 0)
                intervals.Add(days);
        }
        return intervals;
    }

    /// <summary>
    /// Removes outlier intervals that likely represent double releases or skipped releases.
    /// A double release is when two chapters come very close together (e.g., 0-2 days apart)
    /// while the typical cadence is much larger. We merge these with adjacent intervals.
    /// </summary>
    public List<double> RemoveOutlierIntervals(List<double> intervals)
    {
        if (intervals.Count < 3)
            return intervals;

        // Compute median as robust central tendency
        double median = ComputeMedian(intervals);

        // Compute Median Absolute Deviation (MAD) for outlier detection
        var deviations = intervals.Select(i => Math.Abs(i - median)).ToList();
        double mad = ComputeMedian(deviations);
        if (mad == 0)
            mad = median * 0.5; // Fallback if all intervals are identical

        // Filter: keep intervals within median ± 3 * MAD
        // This removes extreme outliers (e.g., a 90-day gap in a weekly series)
        double lowerBound = Math.Max(0, median - 3 * mad);
        double upperBound = median + 3 * mad;

        var filtered = intervals.Where(i => i >= lowerBound && i <= upperBound).ToList();

        // If filtering removed too much, return original
        if (filtered.Count < intervals.Count * 0.5)
            return intervals;

        return filtered.Count > 0 ? filtered : intervals;
    }

    /// <summary>
    /// Computes the median of a list of doubles.
    /// </summary>
    public double ComputeMedian(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;

        if (sorted.Count % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2.0;

        return sorted[mid];
    }

    /// <summary>
    /// Maps a median interval in days to a standard cadence.
    /// </summary>
    public int? MapIntervalToCadence(double medianIntervalDays)
    {
        if (medianIntervalDays <= 0)
            return null;

        if (medianIntervalDays <= WeeklyUpperBound)
            return CadenceWeekly;

        if (medianIntervalDays <= HalfMonthUpperBound)
            return CadenceHalfMonth;

        // Anything above half-month threshold is monthly
        return CadenceMonthly;
    }

    /// <summary>
    /// Fallback: if the series is ONGOING, assume maximum cadence (1 month = 30 days).
    /// Otherwise, leave null (no monitoring needed for completed/hiatus series).
    /// </summary>
    private async Task<int?> FallbackCadenceAsync(Guid seriesId, string name, CancellationToken token = default)
    {
        var series = await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId, token)
            .ConfigureAwait(false);

        if (series == null)
            return null;

        // If ongoing but we can't determine cadence, assume max (1 month)
        if (series.Status == Models.Enums.SeriesStatus.ONGOING)
        {
            // Store the fallback cadence so we don't recompute every time
            var tracked = await _db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, token)
                .ConfigureAwait(false);
            if (tracked != null && tracked.ReleaseCadenceDays == null)
            {
                tracked.ReleaseCadenceDays = CadenceMonthly;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                _logger.LogInformation(
                    "Fallback cadence for ongoing series {Name}: {CadenceDays} days (insufficient data)",
                    name, CadenceMonthly);
            }
            return CadenceMonthly;
        }

        return null;
    }
}
