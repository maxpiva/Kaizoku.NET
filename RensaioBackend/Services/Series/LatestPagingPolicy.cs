namespace RensaioBackend.Services.Series;

/// <summary>
/// Decides when a provider's "latest" refresh stops asking the source for further pages.
/// </summary>
internal static class LatestPagingPolicy
{
    /// <summary>
    /// Upper bound of pages walked in a single run. What was fetched is saved when the run
    /// ends, so the next run meets series it already knows and stops on its own.
    /// </summary>
    public const int MaxPagesPerRun = 10;

    /// <summary>
    /// Whether another page should be requested after <paramref name="page"/>.
    /// </summary>
    /// <param name="page">The page that was just processed (1-based).</param>
    /// <param name="mangasOnPage">How many entries that page returned.</param>
    /// <param name="hasNextPage">What the source reported for that page.</param>
    /// <param name="upToDate">A series already stored with nothing newer online was reached.</param>
    /// <param name="neverDone">Nothing is stored yet for this provider (first run takes one page).</param>
    public static bool ShouldFetchNextPage(int page, int mangasOnPage, bool hasNextPage, bool upToDate, bool neverDone)
    {
        if (upToDate || neverDone)
            return false;
        // The source ran out: an empty page, or it says there is nothing after this one.
        if (mangasOnPage == 0 || !hasNextPage)
            return false;
        return page < MaxPagesPerRun;
    }
}
