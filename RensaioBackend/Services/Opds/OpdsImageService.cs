using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Migration.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Archives;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace RensaioBackend.Services.Opds;

/// <summary>
/// Scoped service for extracting and serving page images from archived chapters for OPDS.
/// Supports .zip, .rar, and .7zip archives with a temp cache.
/// Uses <see cref="OpdsExtractionCoordinator"/> for shared per-user cancellation state
/// and per-page TaskCompletionSource signaling so callers wait only for the specific
/// page they need, not the entire archive.
/// Supports on-the-fly image format conversion based on client capabilities.
/// </summary>
public class OpdsImageService
{
    private readonly OpdsExtractionCoordinator _coordinator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpdsImageService> _logger;
    private readonly IImageFactory _imageFactory;
    private readonly HashCacheService _hashCacheService;
    private readonly AppDbContext _db;
    private readonly ReadStateService _readStateService;
    private readonly SettingsService _settingsService;
    private const int DefaultMaxCachedChapters = 50;

    public OpdsImageService(
        OpdsExtractionCoordinator coordinator,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OpdsImageService> logger,
        IImageFactory imageFactory,
        HashCacheService hashCacheService,
        AppDbContext db,
        ReadStateService readStateService,
        SettingsService settingsService)
    {
        _coordinator = coordinator;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _imageFactory = imageFactory;
        _hashCacheService = hashCacheService;
        _db = db;
        _readStateService = readStateService;
        _settingsService = settingsService;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a page image from a chapter archive.
    /// If the chapter is currently being extracted (by a background preload), this
    /// method will wait only for that specific page to be extracted, not the whole archive.
    /// Supports on-the-fly image format conversion based on client capabilities.
    /// Returns a seekable stream to avoid buffering the entire image in memory.
    /// Also returns the MD5 hash of the final image for ETag support
    /// (lazy-computed only if not already cached).
    /// </summary>
    public async Task<(Stream? imageStream, string? contentType, string? md5Hash)> GetPageImageAsync(
        UserEntity user, Guid seriesId, string language, string chapterFilename, int pageIndex,
        List<string> supportedImageFormats,
        CancellationToken token = default)
    {
        var series = await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId, token);

        if (series == null || string.IsNullOrWhiteSpace(series.StoragePath))
            return (null, null, null);
        string name = series.Title;
        string seriesStoragePath = series.StoragePath;
        string? archivePath = _settingsService.DirectSettings?.ResolveChapterPath(seriesStoragePath, chapterFilename);

        if (archivePath == null)
            return (null, null, null);

        string userKey = OpdsExtractionCoordinator.GetUserKey(user.Username);
        string cacheKey = $"{seriesId}:{language}:{chapterFilename}";
        string cacheDir = GetCacheDirectory(seriesId, language, chapterFilename);

        // ── Fast path: chapter already fully extracted in cache ──
        // Use state.Pages (naturally ordered) to avoid a wasteful Directory.GetFiles + sort
        if (_coordinator.TryGetExtraction(userKey, out var state) &&
            state.ChapterCacheKey == cacheKey &&
            state.Pages.Count > 0)
        {
            if (pageIndex < state.Pages.Count)
            {
                string entryKey = state.Pages[pageIndex].Replace('/', System.IO.Path.DirectorySeparatorChar);
                string? pageFile = ResolvePageFile(cacheDir, entryKey);
                if (pageFile != null)
                {
                    return await ReadAndConvertPageAsync(pageFile, supportedImageFormats, seriesStoragePath, chapterFilename, pageIndex, token);
                }
            }
        }
        else if (OpdsExtractionCoordinator.IsChapterCacheComplete(cacheDir))
        {
            // Fallback: no active state, but the chapter is fully cached — scan it directly.
            // Partial directories (canceled/failed extraction) have no completion marker
            // and fall through to extraction below, which resumes from existing files.
            return await ReadPageFromCacheDirAsync(cacheDir, pageIndex, supportedImageFormats, seriesStoragePath, chapterFilename, token);
        }

        // ── Ensure extraction is running for this chapter ──
        // If there's no active extraction for this exact chapter, cancel any other
        // extraction for this user and start one for the requested chapter.
        // Serialized per chapter so parallel page requests from a reader don't
        // each cancel the others' just-started extraction.
        if (!_coordinator.TryGetExtraction(userKey, out state) ||
            state.ChapterCacheKey != cacheKey ||
            state.ExtractionTask is { IsCompleted: true })
        {
            using (await _coordinator.AcquireChapterLockAsync(cacheKey, token))
            {
                // Double-check under the lock: another request may have fully extracted
                // the chapter (completion marker written) while we waited.
                if (OpdsExtractionCoordinator.IsChapterCacheComplete(cacheDir))
                {
                    return await ReadPageFromCacheDirAsync(cacheDir, pageIndex, supportedImageFormats, seriesStoragePath, chapterFilename, token);
                }

                // Re-check the extraction state — a concurrent request may have started it
                if (!_coordinator.TryGetExtraction(userKey, out state) ||
                    state.ChapterCacheKey != cacheKey ||
                    state.ExtractionTask is { IsCompleted: true })
                {
                    _coordinator.CancelActiveExtraction(userKey);

                    // Setup extraction (DB lookup, pages, signals) and fire the task without awaiting it.
                    // The state is stored in the coordinator so we can wait for the page signal.
                    state = await SetupAndFireExtractionAsync(
                        userKey, seriesId, name, cacheKey, cacheDir, archivePath, chapterFilename, seriesStoragePath, supportedImageFormats);
                }
            }

            if (state == null)
                return (null, null, null);
        }

        // ── Wait for the specific page to be extracted ──
        if (pageIndex < state.Pages.Count)
        {
            string entryKey = state.Pages[pageIndex].Replace('/', System.IO.Path.DirectorySeparatorChar);
            if (state.PageSignals.TryGetValue(entryKey, out var pageTcs))
            {
                _logger.LogDebug("Waiting for page {PageIndex} ({EntryKey}) extraction for chapter {CacheKey}",
                    pageIndex, entryKey, cacheKey);
                try
                {
                    await pageTcs.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Page {PageIndex} extraction failed for chapter {CacheKey}", pageIndex, cacheKey);
                    return (null, null, null);
                }

                // Read the file now that it's ready — run conversion if needed.
                // The extracted file may have a different extension than the archive
                // entry: format conversion replaces the original (e.g. .webp → .jpg).
                string? pageFile = ResolvePageFile(cacheDir, entryKey);
                if (pageFile != null)
                {
                    return await ReadAndConvertPageAsync(pageFile, supportedImageFormats, seriesStoragePath, chapterFilename, pageIndex, token);
                }
                return (null, null, null);
            }
        }

        // ── Fallback: page index out of range or signal not found — wait for full extraction ──
        _logger.LogDebug("Page index {PageIndex} out of range or signal not found for chapter {CacheKey}, waiting for full extraction",
            pageIndex, cacheKey);
        try
        {
            if (state.ExtractionTask != null)
                await state.ExtractionTask.WaitAsync(token);
        }
        catch { /* fall through */ }

        // Last resort: read from cache dir
        if (Directory.Exists(cacheDir))
        {
            return await ReadPageFromCacheDirAsync(cacheDir, pageIndex, supportedImageFormats, seriesStoragePath, chapterFilename, token);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Proactively starts extracting the first unread chapter for a series.
    /// Called after the OPDS series feed has been sent to the client.
    /// If a different chapter is already being extracted (for this user), it is canceled first.
    /// Does nothing if the target chapter is already cached.
    /// Fire-and-forget — creates its own scope for background work.
    /// </summary>
    public void PreloadFirstUnreadChapterAsync(UserEntity user, Guid seriesId, string? language, string seriesStoragePath,
        List<string> supportedImageFormats)
    {
        string userKey = OpdsExtractionCoordinator.GetUserKey(user.Username);
        _ = PreloadFirstUnreadChapterInternalAsync(user, seriesId, language, seriesStoragePath, userKey, supportedImageFormats);
    }

    /// <summary>
    /// Proactively starts extracting the immediate next chapter after the one the user
    /// is currently reading. Called from PostReadingState when progress reaches >80%
    /// or the chapter is marked as completed.
    ///
    /// Waits for any current extraction for this user to finish first, then
    /// extracts the next chapter if it exists and is not already cached.
    /// Fire-and-forget — creates its own scope for background work.
    /// </summary>
    public void PreloadNextChapterAsync(UserEntity user, Guid seriesId, string language,
        decimal currentChapterNumber, string seriesStoragePath,
        List<string> supportedImageFormats)
    {
        string userKey = OpdsExtractionCoordinator.GetUserKey(user.Username);
        _ = PreloadNextChapterInternalAsync(user, seriesId, language, currentChapterNumber,
            seriesStoragePath, userKey, supportedImageFormats);
    }

    /// <summary>
    /// Ensures a specific chapter is loaded into the cache.
    /// If a different chapter is currently being preloaded (for this user),
    /// that preload is canceled and its partial cache deleted.
    /// If the requested chapter is already cached, nothing happens.
    /// If there is an active extraction for this exact chapter, we wait for it.
    /// </summary>
    public Task EnsureChapterIsLoadedAsync(
        UserEntity user, Guid seriesId, string title, string language, string chapterFilename, string seriesStoragePath,
        List<string> supportedImageFormats)
    {
        string userKey = OpdsExtractionCoordinator.GetUserKey(user.Username);
        string cacheKey = $"{seriesId}:{language}:{chapterFilename}";
        string cacheDir = GetCacheDirectory(seriesId, language, chapterFilename);

        // Already fully cached – nothing to do (a partial dir without the marker re-extracts and resumes)
        if (OpdsExtractionCoordinator.IsChapterCacheComplete(cacheDir))
            return Task.CompletedTask;
        // Resolve the full archive path before extraction
        string? archivePath = _settingsService.DirectSettings?.ResolveChapterPath(seriesStoragePath, chapterFilename);
        if (archivePath == null)
        {
            _logger.LogWarning("Archive not found for chapter {Filename} in series {title}",
                chapterFilename, seriesId);
            return Task.CompletedTask;
        }

        // This exact chapter is already being extracted – just wait for it
        if (_coordinator.TryGetExtraction(userKey, out var active) &&
            active.ChapterCacheKey == cacheKey &&
            active.ExtractionTask is { IsCompleted: false } activeTask)
        {
            return activeTask;
        }

        // Cancel any existing preload for this user
        _coordinator.CancelActiveExtraction(userKey);

        // Start extraction with the full archive path
        return StartExtractionAsync(userKey, seriesId, title, cacheKey, cacheDir, archivePath, chapterFilename, seriesStoragePath, supportedImageFormats);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task PreloadFirstUnreadChapterInternalAsync(
        UserEntity user, Guid seriesId, string? language, string seriesStoragePath, string userKey,
        List<string> supportedImageFormats)
    {
        string name = seriesId.ToString();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var readStateService = scope.ServiceProvider.GetRequiredService<ReadStateService>();
            var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(a=>a.Id==seriesId);
            name = series?.Title ?? name;
            var providers = await db.SeriesProviders
                .Where(sp => sp.SeriesId == seriesId)
                .AsNoTracking()
                .ToListAsync();

            if (providers.Count == 0) return;

            var langProviders = !string.IsNullOrEmpty(language)
                ? providers.Where(p => p.Language == language).ToList()
                : providers;

            if (langProviders.Count == 0) return;

            // Collect deduplicated chapters ordered ascending (only those backed by actual files)
            var allChapters = new List<Models.Chapter>();
            foreach (var p in langProviders)
            {
                if (p.Chapters != null)
                    allChapters.AddRange(p.Chapters.Where(c => !string.IsNullOrEmpty(c.Filename)));
            }

            var dedupedChapters = allChapters
                .GroupBy(c => c.ChapterNumber)
                .Select(g => g.First())
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            if (dedupedChapters.Count == 0) return;

            // Find first non-read chapter
            decimal[] completedChapters = readStateService.GetSeriesReadStates(user.Username, seriesStoragePath)
                .Where(rs => rs.IsCompleted)
                .Select(rs => rs.ChapterNumber)
                .ToArray();

            var firstUnread = dedupedChapters
                .FirstOrDefault(c => !Array.Exists(completedChapters, x => x == c.ChapterNumber.GetValueOrDefault()));

            if (firstUnread == null)
                firstUnread = dedupedChapters.First(); // all read – fall back to first

            string chapterFilename = firstUnread.Filename ?? "";
            if (string.IsNullOrWhiteSpace(chapterFilename)) return;

            // Determine language: use the one from the provider that contributed this chapter
            string lang = language ?? "en";
            foreach (var p in langProviders)
            {
                if (p.Chapters?.Any(c => c.Filename == chapterFilename) == true)
                {
                    lang = p.Language ?? "en";
                    break;
                }
            }
            string cacheKey = $"{seriesId}:{lang}:{chapterFilename}";
            string cacheDir = GetCacheDirectory(seriesId, lang, chapterFilename);

            // Already fully cached – nothing to do
            if (OpdsExtractionCoordinator.IsChapterCacheComplete(cacheDir))
                return;

            // An extraction is already running for this user (likely the chapter
            // they are reading right now) — a background preload must not steal it
            if (_coordinator.TryGetExtraction(userKey, out var active) &&
                active.ExtractionTask is { IsCompleted: false })
                return;

            // Resolve the full archive path before extraction
            string? archivePath = _settingsService.DirectSettings?.ResolveChapterPath(seriesStoragePath, chapterFilename);
            if (archivePath == null)
            {
                _logger.LogWarning("Archive not found for chapter {Filename} in series {name}",
                    chapterFilename, name);
                return;
            }

            // Clear any completed extraction state for this user
            _coordinator.CancelActiveExtraction(userKey);

            await StartExtractionAsync(userKey, seriesId, name, cacheKey, cacheDir, archivePath, chapterFilename, seriesStoragePath, supportedImageFormats);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Preload canceled for series {name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background preload failed for series {name}", name);
        }
    }

    private async Task PreloadNextChapterInternalAsync(
        UserEntity user, Guid seriesId, string language,
        decimal currentChapterNumber, string seriesStoragePath, string userKey,
        List<string> supportedImageFormats)
    {
        string name = seriesId.ToString();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(a => a.Id == seriesId);
            name = series?.Title ?? name;

            var providers = await db.SeriesProviders
                .Where(sp => sp.SeriesId == seriesId)
                .AsNoTracking()
                .ToListAsync();

            if (providers.Count == 0) return;

            var langProviders = providers.Where(p => p.Language == language).ToList();
            if (langProviders.Count == 0) return;

            // Collect deduplicated chapters ordered ascending (only those backed by actual files)
            var allChapters = new List<Models.Chapter>();
            foreach (var p in langProviders)
            {
                if (p.Chapters != null)
                    allChapters.AddRange(p.Chapters.Where(c => !string.IsNullOrEmpty(c.Filename)));
            }

            var dedupedChapters = allChapters
                .GroupBy(c => c.ChapterNumber)
                .Select(g => g.First())
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            if (dedupedChapters.Count == 0) return;

            // Find the next chapter after the current one
            var nextChapter = dedupedChapters
                .FirstOrDefault(c => c.ChapterNumber.GetValueOrDefault() > currentChapterNumber);

            if (nextChapter == null) return; // No next chapter

            string chapterFilename = nextChapter.Filename ?? "";
            if (string.IsNullOrWhiteSpace(chapterFilename)) return;

            // Determine language from the provider that owns this chapter
            string lang = language;
            foreach (var p in langProviders)
            {
                if (p.Chapters?.Any(c => c.Filename == chapterFilename) == true)
                {
                    lang = p.Language ?? language;
                    break;
                }
            }

            string cacheKey = $"{seriesId}:{lang}:{chapterFilename}";
            string cacheDir = GetCacheDirectory(seriesId, lang, chapterFilename);

            // Already fully cached – nothing to do
            if (OpdsExtractionCoordinator.IsChapterCacheComplete(cacheDir))
                return;

            // Wait for any current extraction for this user to finish
            if (_coordinator.TryGetExtraction(userKey, out var activeState) &&
                activeState.ExtractionTask is { IsCompleted: false })
            {
                _logger.LogDebug("Waiting for current extraction to finish before preloading next chapter for {User}", user.Username);
                try { await activeState.ExtractionTask.WaitAsync(CancellationToken.None); }
                catch { /* swallow — extraction failed, proceed anyway */ }
            }

            // Resolve the full archive path before extraction
            string? archivePath = _settingsService.DirectSettings?.ResolveChapterPath(seriesStoragePath, chapterFilename);
            if (archivePath == null)
            {
                _logger.LogWarning("Archive not found for next chapter {Filename} in series {name}",
                    chapterFilename, name);
                return;
            }

            // If a new extraction started while we were waiting (the user began
            // reading something), don't steal it for a background preload
            if (_coordinator.TryGetExtraction(userKey, out var newActive) &&
                newActive.ExtractionTask is { IsCompleted: false })
                return;

            // Start extraction of the next chapter
            _coordinator.CancelActiveExtraction(userKey);
            await StartExtractionAsync(userKey, seriesId, name, cacheKey, cacheDir, archivePath, chapterFilename, seriesStoragePath, supportedImageFormats);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Next-chapter preload canceled for series {name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Next-chapter preload failed for series {name}", name);
        }
    }

    /// <summary>
    /// Sets up extraction state (DB lookup, pages map, per-page signals) and fires the
    /// extraction task without awaiting it. Returns the state so callers can wait for
    /// specific page signals or the full extraction task.
    /// Returns null if the chapter cannot be found in the DB.
    /// </summary>
    private async Task<OpdsExtractionCoordinator.SeriesExtractionState?> SetupAndFireExtractionAsync(
        string userKey, Guid seriesId, string name, string cacheKey, string cacheDir,
        string archivePath, string chapterFilename, string seriesStoragePath,
        List<string> supportedImageFormats)
    {
        // ── Step 1: Query DB for the chapter and its Pages map ──
        List<string> pages = [];

        // Find the SeriesProvider whose chapters contain a match by filename
        var providers = await _db.SeriesProviders
            .Where(sp => sp.SeriesId == seriesId)
            .ToListAsync();

        Models.Chapter? chapter = null;
        SeriesProviderEntity? provider = null;

        foreach (var sp in providers)
        {
            var match = sp.Chapters?.FirstOrDefault(c => c.Filename == chapterFilename);
            if (match != null)
            {
                chapter = match;
                provider = sp;
                break;
            }
        }

        if (chapter == null)
        {
            _logger.LogWarning("Chapter {Filename} not found in DB for series {name}", chapterFilename, name);
            return null;
        }

        // Populate Pages if empty
        if (chapter.Pages.Count == 0)
        {
            _logger.LogDebug("Pages list empty for chapter {Filename}, populating from archive", chapterFilename);
            var images = ArchiveHelperService.GetImageFiles(archivePath);
            chapter.Pages = images;
            chapter.PageCount = images.Count;
            _db.Touch(provider!, c => c.Chapters);
            await _db.SaveChangesAsync();
        }

        pages = chapter.Pages;

        // ── Step 2: Create state with per-page signals ──
        var cts = new CancellationTokenSource();
        var pageSignals = new ConcurrentDictionary<string, TaskCompletionSource>();
        foreach (var page in pages)
        {
            string entryKey = page.Replace('/', System.IO.Path.DirectorySeparatorChar);
            pageSignals[entryKey] = new TaskCompletionSource();
        }

        var state = new OpdsExtractionCoordinator.SeriesExtractionState
        {
            Cts = cts,
            ChapterCacheKey = cacheKey,
            CacheDir = cacheDir,
            Pages = pages,
            PageSignals = pageSignals,
            SupportedImageFormats = supportedImageFormats,
        };

        _coordinator.RegisterExtraction(userKey, state);

        state.ExtractionTask = ExtractWithPerPageSignalingAsync(archivePath, cacheDir, cts, state, userKey);

        return state;
    }

    /// <summary>
    /// Starts extraction of a chapter archive and awaits completion for error logging.
    /// Used by preload methods that want to observe the full extraction lifecycle.
    /// </summary>
    private async Task StartExtractionAsync(
        string userKey, Guid seriesId, string name, string cacheKey, string cacheDir,
        string archivePath, string chapterFilename, string seriesStoragePath,
        List<string> supportedImageFormats)
    {
        var state = await SetupAndFireExtractionAsync(
            userKey, seriesId, name, cacheKey, cacheDir, archivePath, chapterFilename, seriesStoragePath, supportedImageFormats);

        if (state?.ExtractionTask == null)
            return;

        try
        {
            await state.ExtractionTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Extraction canceled for series {name}, chapter {CacheKey}", name, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extraction failed for series {name}, chapter {CacheKey}", name, cacheKey);
        }
    }

    /// <summary>
    /// Extracts an archive entry-by-entry, signaling per-page TaskCompletionSource
    /// as each matching entry finishes writing to cache.
    /// After writing each entry, runs image format conversion if the client
    /// does not support the original format.
    /// Uses IServiceScopeFactory for scoped services (IImageFactory) since this runs
    /// as a background task that may outlive the request scope.
    /// </summary>
    private async Task ExtractWithPerPageSignalingAsync(
        string archivePath, string cacheDir, CancellationTokenSource cts,
        OpdsExtractionCoordinator.SeriesExtractionState state, string userKey)
    {
        Directory.CreateDirectory(cacheDir);

        // Build a lookup set for faster matching
        var pageSet = new HashSet<string>(state.Pages, StringComparer.OrdinalIgnoreCase);

        // Serialize extraction per chapter: two users (or a raced restart) targeting
        // the same chapter share the cache directory, and concurrent writes to the
        // same files cause sharing violations on Windows. The second extractor
        // acquires the lock after the first finishes and cheaply skips existing files.
        var chapterLock = _coordinator.GetChapterLock(state.ChapterCacheKey);
        bool lockTaken = false;
        bool anyPageFailed = false;

        try
        {
            await chapterLock.WaitAsync(cts.Token);
            lockTaken = true;

            var archive = await SharpCompress.Archives.ArchiveFactory.OpenAsyncArchive(archivePath);
            var allEntries = archive.EntriesAsync;

            await foreach (var entry in allEntries)
            {
                if (entry.IsDirectory)
                    continue;

                cts.Token.ThrowIfCancellationRequested();

                string entryKey = entry.Key?.Replace('/', System.IO.Path.DirectorySeparatorChar) ?? "";
                string fullPath = System.IO.Path.Combine(cacheDir, entryKey);
                bool isPage = pageSet.Contains(entryKey);

                // Resume support: skip entries already extracted (or extracted+converted)
                // by a previous run of this chapter
                if (ResolvePageFile(cacheDir, entryKey) != null)
                {
                    if (isPage && state.PageSignals.TryGetValue(entryKey, out var doneTcs))
                        doneTcs.TrySetResult();
                    continue;
                }

                try
                {
                    // Create subdirectories if needed
                    string? dir = System.IO.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    // Write to a temp file and move into place so a cancel/crash
                    // never leaves a truncated page behind for a later resume to trust
                    string tempPath = fullPath + ".tmp";
                    try
                    {
                        await entry.WriteToFileAsync(tempPath, null, cts.Token);
                        File.Move(tempPath, fullPath, overwrite: true);
                    }
                    catch
                    {
                        try { File.Delete(tempPath); } catch { /* best effort */ }
                        throw;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad entry must not fail the rest of the chapter:
                    // fail only this page's signal and keep extracting
                    _logger.LogWarning(ex, "Failed to extract entry {EntryKey} from {ArchivePath}", entryKey, archivePath);
                    if (isPage)
                    {
                        anyPageFailed = true;
                        if (state.PageSignals.TryGetValue(entryKey, out var failedTcs))
                            failedTcs.TrySetException(ex);
                    }
                    continue;
                }

                // Convert image format if needed and client has format preferences
                if (state.SupportedImageFormats.Count > 0 && isPage)
                {
                    try
                    {
                        // Create a scope for IImageFactory since extraction runs in background
                        using var scope = _scopeFactory.CreateScope();
                        var imageFactory = scope.ServiceProvider.GetRequiredService<IImageFactory>();
                        string convertedPath = await ImageExtensions.CreateImageFileFormatIfNeeded(
                            imageFactory, fullPath, state.SupportedImageFormats,
                            ImageExtensions.EncodeFormat.JPEG, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Image format conversion failed for {EntryKey}, serving original", entryKey);
                    }
                }

                // Signal this specific page if it's in our Pages list
                if (isPage && state.PageSignals.TryGetValue(entryKey, out var tcs))
                {
                    tcs.TrySetResult();
                }
            }

            // After all entries processed, signal any remaining pages that weren't found
            foreach (var kvp in state.PageSignals)
            {
                if (kvp.Value.TrySetException(new InvalidOperationException(
                        $"Page entry '{kvp.Key}' was not found in the archive")))
                {
                    anyPageFailed = true;
                }
            }

            // Only manage cache if extraction completed successfully (not canceled)
            if (!cts.Token.IsCancellationRequested)
            {
                if (!anyPageFailed)
                    OpdsExtractionCoordinator.MarkChapterCacheComplete(cacheDir);
                await ManageCacheSizeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // On cancel, set all pending signals as canceled.
            // The partial cache is kept — files are written atomically, so a later
            // extraction of this chapter resumes from them.
            foreach (var kvp in state.PageSignals)
            {
                kvp.Value.TrySetCanceled(cts.Token);
            }
            throw;
        }
        catch (Exception ex)
        {
            // On failure (e.g. the archive itself can't be opened), set all pending
            // signals with the exception. Already-extracted pages stay on disk.
            foreach (var kvp in state.PageSignals)
            {
                kvp.Value.TrySetException(ex);
            }
            throw;
        }
        finally
        {
            if (lockTaken)
                chapterLock.Release();

            // Only remove our own state if it hasn't been replaced
            if (_coordinator.TryGetExtraction(userKey, out var current) &&
                ReferenceEquals(current, state))
            {
                _coordinator.TryRemoveExtraction(userKey, out _);
            }

            cts.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Image format conversion helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a page file and optionally converts it to a format the client supports.
    /// If the file's extension is already in <paramref name="supportedImageFormats"/>,
    /// returns the file as-is. Otherwise, attempts conversion via
    /// <see cref="ImageExtensions.CreateImageFileFormatIfNeeded"/>.
    /// Returns a seekable FileStream to avoid buffering the entire image in memory.
    /// Also returns the MD5 hash of the final image for ETag support
    /// (lazy-computed only if not already cached).
    /// </summary>
    private async Task<(Stream? imageStream, string? contentType, string? md5Hash)> ReadAndConvertPageAsync(
        string pageFile, List<string> supportedImageFormats,
        string seriesStoragePath, string chapterFilename, int pageIndex,
        CancellationToken token)
    {
        string finalFile = pageFile;
        if (supportedImageFormats.Count > 0)
        {
            string ext = System.IO.Path.GetExtension(pageFile).TrimStart('.').ToLowerInvariant();
            if (!supportedImageFormats.Contains(ext))
            {
                try
                {
                    finalFile = await ImageExtensions.CreateImageFileFormatIfNeeded(
                        _imageFactory, pageFile, supportedImageFormats,
                        ImageExtensions.EncodeFormat.JPEG, token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image format conversion failed for {PageFile}, serving original", pageFile);
                }
            }
        }

        string contentType = GetContentType(finalFile);

        // Lazily compute/save MD5 hash for ETag support
        string md5Hash = _hashCacheService.ComputeAndSavePageHash(
            seriesStoragePath, chapterFilename, pageIndex, contentType, finalFile);

        // Open as a seekable FileStream — caller (controller) will dispose it after sending
        Stream stream = new FileStream(finalFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return (stream, contentType, md5Hash);
    }

    /// <summary>
    /// Reads a page from a cache directory, accounting for multiple file extensions
    /// due to format conversions. Finds the best matching file for the client's
    /// supported formats, or converts if needed.
    /// Returns a seekable FileStream to avoid buffering the entire image in memory.
    /// Also returns the MD5 hash of the final image for ETag support
    /// (lazy-computed only if not already cached).
    /// </summary>
    private async Task<(Stream? imageStream, string? contentType, string? md5Hash)> ReadPageFromCacheDirAsync(
        string cacheDir, int pageIndex, List<string> supportedImageFormats,
        string seriesStoragePath, string chapterFilename,
        CancellationToken token)
    {
        // Recursive: archives can contain images inside subdirectories
        string[] allFiles = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
            .Where(f => ArchiveHelperService.ArchiveIsImage(f))
            .OrderBy(f => f, new NaturalSortComparer())
            .ToArray();

        if (allFiles.Length == 0 || pageIndex >= allFiles.Length)
            return (null, null, null);

        // Group files by their extension-less path relative to the cache dir to
        // handle multiple format variants of the same page
        var groupedFiles = allFiles
            .GroupBy(f => System.IO.Path.ChangeExtension(System.IO.Path.GetRelativePath(cacheDir, f), null),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, new NaturalSortComparer())
            .ToList();

        if (pageIndex >= groupedFiles.Count)
            return (null, null, null);

        var pageVariants = groupedFiles[pageIndex].ToList();

        // If client has format preferences, find the best matching variant
        string selectedFile;
        if (supportedImageFormats.Count > 0)
        {
            // Try to find a variant whose extension is in the supported list
            selectedFile = pageVariants.FirstOrDefault(f =>
            {
                string ext = System.IO.Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
                return supportedImageFormats.Contains(ext);
            }) ?? pageVariants.First(); // Fall back to first variant

            // If the selected file's extension is not supported, convert it
            string ext = System.IO.Path.GetExtension(selectedFile).TrimStart('.').ToLowerInvariant();
            if (!supportedImageFormats.Contains(ext))
            {
                try
                {
                    selectedFile = await ImageExtensions.CreateImageFileFormatIfNeeded(
                        _imageFactory, selectedFile, supportedImageFormats,
                        ImageExtensions.EncodeFormat.JPEG, token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image format conversion failed for {SelectedFile}, serving original", selectedFile);
                }
            }
        }
        else
        {
            selectedFile = pageVariants.First();
        }

        string contentType = GetContentType(selectedFile);

        // Lazily compute/save MD5 hash for ETag support
        string md5Hash = _hashCacheService.ComputeAndSavePageHash(
            seriesStoragePath, chapterFilename, pageIndex, contentType, selectedFile);

        // Open as a seekable FileStream — caller (controller) will dispose it after sending
        Stream stream = new FileStream(selectedFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return (stream, contentType, md5Hash);
    }

    /// <summary>
    /// Resolves the on-disk file for an archive entry key, accounting for format
    /// conversion: <see cref="ImageExtensions.CreateImageFileFormatIfNeeded"/> deletes
    /// the original after converting, so the cached file may have a different
    /// extension (e.g. entry "001.webp" exists on disk only as "001.jpg").
    /// Returns null if neither the original nor a converted variant exists.
    /// </summary>
    private static string? ResolvePageFile(string cacheDir, string entryKey)
    {
        string exact = System.IO.Path.Combine(cacheDir, entryKey);
        if (File.Exists(exact))
            return exact;

        string? dir = System.IO.Path.GetDirectoryName(exact);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;

        string baseName = System.IO.Path.GetFileNameWithoutExtension(exact);
        foreach (string candidate in Directory.EnumerateFiles(dir, baseName + ".*"))
        {
            if (ArchiveHelperService.ArchiveIsImage(candidate) &&
                string.Equals(System.IO.Path.GetFileNameWithoutExtension(candidate), baseName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return null;
    }



    // ──────────────────────────────────────────────────────────────────────────
    // Cache management
    // ──────────────────────────────────────────────────────────────────────────

    private async Task ManageCacheSizeAsync()
    {
        string tempFolder = _configuration.GetValue<string>("TempFolder", "");
        if (string.IsNullOrWhiteSpace(tempFolder))
            tempFolder = System.IO.Path.GetTempPath();
        string opdsCache = System.IO.Path.Combine(tempFolder, "opds-cache");

        if (!Directory.Exists(opdsCache))
            return;

        int maxChapters = _configuration.GetValue<int>("OpdsTempCacheMaxChapters", DefaultMaxCachedChapters);

        // Multiply cache limit by the number of active users
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            int userCount = await db.Users.CountAsync();
            if (userCount > 1)
                maxChapters *= userCount;
        }
        catch
        {
            // Fall back to configured limit
        }

        // Count total chapter cache directories
        var chapterDirs = Directory.GetDirectories(opdsCache, "*", SearchOption.AllDirectories)
            .Where(d => Directory.GetFiles(d).Length > 0)
            .OrderBy(d => Directory.GetLastWriteTimeUtc(d))
            .ToList();

        while (chapterDirs.Count > maxChapters)
        {
            var oldest = chapterDirs.First();
            try
            {
                // Remove the completion marker first: if the recursive delete then
                // fails partway (a page file is open on Windows), the leftover
                // directory is correctly seen as partial and re-extracted, instead
                // of being trusted as a complete chapter with missing pages
                File.Delete(System.IO.Path.Combine(oldest, OpdsExtractionCoordinator.CompleteMarkerFileName));
                Directory.Delete(oldest, true);
            }
            catch { }
            chapterDirs.RemoveAt(0);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Utility methods
    // ──────────────────────────────────────────────────────────────────────────

    private string GetCacheDirectory(Guid seriesId, string language, string chapterFilename)
    {
        string base64Filename = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(chapterFilename))
            .TrimEnd('=');

        string tempFolder = _configuration.GetValue<string>("TempFolder", "");
        if (string.IsNullOrWhiteSpace(tempFolder))
            tempFolder = System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(tempFolder, "opds-cache",
            seriesId.ToString("N"), language, base64Filename);
    }

    private static string GetContentType(string filePath)
    {
        string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".jxl" => "image/jxl",
            ".jp2" => "image/jp2",
            ".heic" or ".heif" => "image/heic",
            _ => "application/octet-stream"
        };
    }
}

/// <summary>
/// Natural sort comparer for file names (e.g., page_2.jpg before page_10.jpg).
/// </summary>
public class NaturalSortComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        return CompareStrings(x, y);
    }

    private static int CompareStrings(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                string numA = "", numB = "";
                while (i < a.Length && char.IsDigit(a[i])) numA += a[i++];
                while (j < b.Length && char.IsDigit(b[j])) numB += b[j++];

                if (long.TryParse(numA, out long nA) && long.TryParse(numB, out long nB))
                {
                    if (nA != nB) return nA.CompareTo(nB);
                }
            }
            else
            {
                if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                i++; j++;
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}
