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
    /// Cancels the active extraction (if any) for the given user and deletes
    /// the partial cache directory so a fresh extraction can start cleanly.
    /// </summary>
    public void CancelActiveExtraction(string userKey)
    {
        if (_activeUserExtractions.TryRemove(userKey, out var state))
        {
            if (!state.Cts.IsCancellationRequested)
            {
                state.Cts.Cancel();
            }

            // Delete partial cache directory (if it was being extracted)
            if (!string.IsNullOrEmpty(state.CacheDir) && Directory.Exists(state.CacheDir))
            {
                DeleteCacheDirectory(state.CacheDir);
            }

            // Wait briefly for the task to finish cleanup, but don't block long
            if (state.ExtractionTask != null)
            {
                try { state.ExtractionTask.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
                catch { /* swallow */ }
            }

            state.Cts.Dispose();
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
    /// Deletes a cache directory recursively, best-effort.
    /// </summary>
    public static void DeleteCacheDirectory(string cacheDir)
    {
        try
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
        catch { /* best effort */ }
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