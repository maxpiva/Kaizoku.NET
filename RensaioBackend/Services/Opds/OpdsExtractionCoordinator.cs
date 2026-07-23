using System.Collections.Concurrent;

namespace RensaioBackend.Services.Opds;

/// <summary>
/// Singleton coordinator for per-user chapter extraction state.
/// Owns all shared mutable state: active extractions, chapter locks, cancellation.
/// Has no DI dependencies — pure state management.
/// </summary>
public class OpdsExtractionCoordinator
{
    // ── Per-chapter extraction locks (prevent concurrent extraction of the same chapter) ──
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _chapterLocks = new();
    private readonly object _lockDictLock = new();

    // ── Per-user active extraction state ──
    private readonly ConcurrentDictionary<string, SeriesExtractionState> _activeUserExtractions = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Extraction State Access
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers (or replaces) an extraction state for the given user key.
    /// </summary>
    public void RegisterExtraction(string userKey, SeriesExtractionState state)
    {
        _activeUserExtractions[userKey] = state;
    }

    /// <summary>
    /// Attempts to get the active extraction state for a user key.
    /// </summary>
    public bool TryGetExtraction(string userKey, out SeriesExtractionState? state)
    {
        return _activeUserExtractions.TryGetValue(userKey, out state);
    }

    /// <summary>
    /// Attempts to remove and return the extraction state for a user key.
    /// </summary>
    public bool TryRemoveExtraction(string userKey, out SeriesExtractionState? state)
    {
        return _activeUserExtractions.TryRemove(userKey, out state);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancellation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancels the active extraction (if any) for the given user.
    /// The partial cache directory is intentionally kept: extracted pages are
    /// written atomically, so a later extraction of the same chapter resumes
    /// from them instead of re-extracting (deleting here also fails on Windows
    /// while a page file is open, stranding a half-deleted directory).
    /// The extraction task owns and disposes its own CancellationTokenSource.
    /// </summary>
    public void CancelActiveExtraction(string userKey)
    {
        if (_activeUserExtractions.TryRemove(userKey, out var state))
        {
            try
            {
                if (!state.Cts.IsCancellationRequested)
                {
                    state.Cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Extraction already finished and disposed its CTS
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Chapter Locks
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or creates a per-chapter semaphore to prevent concurrent extraction.
    /// </summary>
    public SemaphoreSlim GetChapterLock(string cacheKey)
    {
        lock (_lockDictLock)
        {
            if (!_chapterLocks.TryGetValue(cacheKey, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _chapterLocks[cacheKey] = semaphore;
            }
            return semaphore;
        }
    }

    /// <summary>
    /// Acquires a per-chapter async lock to prevent concurrent extraction of the same chapter.
    /// Other threads attempting to extract the same chapter will wait here.
    /// Returns an <see cref="IDisposable"/> that releases the lock when disposed.
    /// </summary>
    public async Task<IDisposable> AcquireChapterLockAsync(string cacheKey, CancellationToken token = default)
    {
        var semaphore = GetChapterLock(cacheKey);
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        return new ChapterLockReleaser(this, cacheKey, semaphore);
    }

    /// <summary>
    /// Releases a per-chapter lock acquired via <see cref="AcquireChapterLockAsync"/>.
    /// </summary>
    private sealed class ChapterLockReleaser : IDisposable
    {
        private readonly OpdsExtractionCoordinator _parent;
        private readonly string _cacheKey;
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public ChapterLockReleaser(OpdsExtractionCoordinator parent, string cacheKey, SemaphoreSlim semaphore)
        {
            _parent = parent;
            _cacheKey = cacheKey;
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _semaphore.Release();
            _disposed = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Utilities
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives the user key used for per-user extraction tracking.
    /// </summary>
    public static string GetUserKey(string username) => username;

    /// <summary>
    /// Marker file written after a chapter finishes extracting with no failed pages.
    /// A cache directory without this marker is partial (canceled/failed/legacy)
    /// and must not be trusted for index-based page lookups.
    /// </summary>
    public const string CompleteMarkerFileName = ".complete";

    /// <summary>
    /// True if the chapter cache directory was fully extracted.
    /// </summary>
    public static bool IsChapterCacheComplete(string cacheDir)
    {
        return File.Exists(Path.Combine(cacheDir, CompleteMarkerFileName));
    }

    /// <summary>
    /// Marks a chapter cache directory as fully extracted, best-effort.
    /// </summary>
    public static void MarkChapterCacheComplete(string cacheDir)
    {
        try
        {
            File.WriteAllText(Path.Combine(cacheDir, CompleteMarkerFileName), "");
        }
        catch { /* best effort — worst case the chapter re-extracts (and resumes) */ }
    }

    /// <summary>
    /// Tracks the state of a per-user chapter extraction operation.
    /// </summary>
    public class SeriesExtractionState
    {
        public CancellationTokenSource Cts { get; set; } = new();
        /// <summary>Cache key of the chapter being extracted (seriesId:language:chapterFilename).</summary>
        public string ChapterCacheKey { get; set; } = "";
        /// <summary>Cache directory of the chapter being extracted.</summary>
        public string CacheDir { get; set; } = "";
        /// <summary>The extraction task, so callers can await completion.</summary>
        public Task? ExtractionTask { get; set; }

        /// <summary>
        /// Ordered list of image entry keys from the archive (matching Chapter.Pages).
        /// </summary>
        public List<string> Pages { get; set; } = [];

        /// <summary>
        /// Per-entry-key signals. Set when that specific page finishes writing to cache.
        /// </summary>
        public ConcurrentDictionary<string, TaskCompletionSource> PageSignals { get; set; } = new();

        /// <summary>
        /// Image formats supported by the client that triggered this extraction.
        /// </summary>
        public List<string> SupportedImageFormats { get; set; } = [];
    }
}