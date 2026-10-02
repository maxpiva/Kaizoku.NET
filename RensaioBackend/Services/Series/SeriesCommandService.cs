using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Metadata;
using RensaioBackend.Services.Opds;
using RensaioBackend.Services.Settings;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using ExtensionChapter = Mihon.ExtensionsBridge.Models.Extensions.Chapter;
using ExtensionManga = Mihon.ExtensionsBridge.Models.Extensions.Manga;

namespace RensaioBackend.Services.Series
{
    /// <summary>
    /// Service responsible for series command operations (Create, Update, Delete)
    /// </summary>
    public class SeriesCommandService
    {
        private readonly AppDbContext _db;
        private readonly SettingsService _settings;
        private readonly ArchiveHelperService _archiveHelper;        private readonly SeriesProviderService _providerService;

        private readonly ILogger<SeriesCommandService> _logger;
        private readonly DownloadCommandService _downloadCommand;
        private readonly MihonBridgeService _mihon;
        private readonly ThumbCacheService _cache;
        private readonly JobManagementService _jobManagement;
        private readonly CadenceCalculationService _cadenceService;
        private readonly SeriesStateService _stateService;
        private readonly HashCacheService _hashCache;
        private readonly MetadataLinkEngine _metadataLinkEngine;
        private readonly IServiceScopeFactory _scopeFactory;

        public SeriesCommandService(AppDbContext db, SettingsService settings, ArchiveHelperService archiveHelper,
            SeriesProviderService providerService, ILogger<SeriesCommandService> logger,
            DownloadCommandService downloadCommand, MihonBridgeService mihon, ThumbCacheService cache,
            JobManagementService jobManagement,
            CadenceCalculationService cadenceService,
            SeriesStateService stateService,
            HashCacheService hashCache,
            MetadataLinkEngine metadataLinkEngine,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _settings = settings;
            _archiveHelper = archiveHelper;
            _providerService = providerService;
            _logger = logger;
            _downloadCommand = downloadCommand;
            _mihon = mihon;
            _cache = cache;
            _jobManagement = jobManagement;
            _cadenceService = cadenceService;
            _stateService = stateService;
            _hashCache = hashCache;
            _metadataLinkEngine = metadataLinkEngine;
            _scopeFactory = scopeFactory;
          }

        /// <summary>
        /// Adds a new series to the database
        /// </summary>
        /// <param name="ProviderSeriesDetails">Full series information to add</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The ID of the created series</returns>
        public async Task<Guid> AddSeriesAsync(AugmentedResponseDto ProviderSeriesDetails, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (ProviderSeriesDetails == null || ProviderSeriesDetails.Series.Count == 0)
            {
                throw new ArgumentException("No series provided to add");
            }

            using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                var paths = await _db.GetPathsAsync(token).ConfigureAwait(false);
                string? existingThumb = null;
                List<SeriesProviderEntity> existingProviders = [];
                Models.Database.SeriesEntity? dbSeries = null;
                
                if (ProviderSeriesDetails.ExistingSeriesId.HasValue)
                {
                    dbSeries = await _db.Series.FirstAsync(s => s.Id == ProviderSeriesDetails.ExistingSeriesId, token)
                        .ConfigureAwait(false);
                    ProviderSeriesDetails.StorageFolderPath = dbSeries.StoragePath;
                }
                else
                {
                    dbSeries = await FindExistingSeriesAsync(ProviderSeriesDetails, settings, paths, token);
                    if (dbSeries != null)
                        existingThumb = dbSeries.ThumbnailUrl;
                }

                if (dbSeries != null)
                {
                    existingProviders = await _db.SeriesProviders.Where(a => a.SeriesId == dbSeries.Id)
                        .ToListAsync(token).ConfigureAwait(false);
                }

                existingProviders = await ProcessSeriesProvidersAsync(ProviderSeriesDetails, existingProviders, token).ConfigureAwait(false);

                dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, existingProviders,
                    ProviderSeriesDetails.StorageFolderPath, ProviderSeriesDetails.DisableJobs, ProviderSeriesDetails.StartChapter, token).ConfigureAwait(false);
                
                existingProviders.ForEach(a => a.SeriesId = dbSeries.Id);
                existingProviders.CalculateContinueAfterChapter(ProviderSeriesDetails.StartChapter);
                
                // Populate Pages and PageCount for all chapters from physical archive files
                string seriesBasePath = Path.Combine(settings.StorageFolder, dbSeries.StoragePath);
                foreach (var provider in existingProviders)
                {
                    provider.PopulateChapterPageCounts(seriesBasePath);
                }
                
                await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                    existingProviders, [], token).ConfigureAwait(false);
                
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                
                await _providerService.RescheduleIfNeededAsync(existingProviders, true, dbSeries.PauseDownloads, token)
                    .ConfigureAwait(false);
                
                await _stateService.SyncToRensaioJsonAsync(dbSeries.Id, token).ConfigureAwait(false);
                
                if (existingThumb != dbSeries.ThumbnailUrl)
                {
                    await _archiveHelper.WriteComicThumbnailAsync(dbSeries, token).ConfigureAwait(false);
                }

                // Fire-and-forget on-add metadata automatch. The transaction is committed and the
                // series row is guaranteed to exist, so the app-wide MetadataLinkEngine can safely
                // link this new series across all metadata providers + the in-memory repository.
                // Independent of the background scan; best-effort (failures never roll back the add).
                //
                // IMPORTANT: this runs in its OWN service scope with no request token. The engine
                // and DbContext here are request-scoped and get disposed the moment the HTTP call
                // returns — using them would cancel/abort the mapping before it completes. Creating
                // a fresh scope (same pattern as MetadataBackgroundScanService.TriggerAsync) lets
                // the mapping always run to completion after the series is created.
                try
                {
                    var newSeriesId = dbSeries.Id;
                    // Intentionally fire-and-forget: run in a detached scope so the mapping is not
                    // tied to (and cannot be cancelled by) the request that created the series.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var engine = scope.ServiceProvider.GetRequiredService<MetadataLinkEngine>();
                            await engine.LinkSeriesAsync(newSeriesId, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "On-add metadata automatch failed for series {SeriesId}", newSeriesId);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to schedule on-add metadata automatch for series {SeriesId}", dbSeries.Id);
                }

                _logger.LogInformation("Added series '{title}' (id {SeriesId}) from {ProviderCount} provider(s).",
                    dbSeries.Title, dbSeries.Id, existingProviders.Count);

                return dbSeries.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AddSeries: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Updates an existing series
        /// </summary>
        /// <param name="series">Series information to update</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Updated series extended information</returns>
        public async Task<SeriesExtendedDto> UpdateSeriesAsync(SeriesExtendedDto series, CancellationToken token = default)
        {
            if (series == null || series.Id == Guid.Empty)
            {
                throw new ArgumentException("Invalid series data provided for update");
            }

            Models.Database.SeriesEntity? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == series.Id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                throw new KeyNotFoundException($"Series with ID {series.Id} not found");
            }

            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            string existingThumb = dbSeries.ThumbnailUrl;

            // ── 1. Storage path change → physical move (before any DB mutation) ──
            // The requested path is the *relative* storage path (e.g. "Manga/One Piece").
            // When different from the current path, move the whole folder across the
            // filesystem with rollback and rewrite queued download paths.
            if (!string.IsNullOrWhiteSpace(series.StoragePath) &&
                !string.Equals(series.StoragePath, dbSeries.StoragePath, StringComparison.Ordinal))
            {
                await MoveSeriesStorageAsync(dbSeries, series.StoragePath, token).ConfigureAwait(false);
            }

            // ── 2. Manual Type override ──
            // The DTO always round-trips the current value; only overwrite when the caller
            // explicitly supplied a non-blank type so refresh-driven nulls never erase it.
            if (!string.IsNullOrWhiteSpace(series.Type))
            {
                dbSeries.Type = series.Type;
            }

            // ── 3. Manual Title semantics ──
            // "Edit title" un-matches every source as the title provider: it clears the
            // IsTitle flag on all sources and stores the manual title as canonical. If the
            // user later toggles a source back to "use as title", UpdateProviderSettings
            // re-flags it and ConsolidateDBSeriesFromProvidersAsync restores that source's
            // title — the manual title is then replaced by the source's (source wins).
            // To decide, look at the effectively-selected title source *after* applying the
            // DTO's provider flags (UpdateProviderSettings runs below), because a stale DTO
            // (e.g. an old client that still has the previously-selected source flagged) must
            // not silently re-apply the source title over a freshly typed one.
            UpdateProviderSettings(series, dbSeries);
            bool anyTitleSourceAfterUpdate = dbSeries.Sources.Any(a => a.IsTitle);

            if (!anyTitleSourceAfterUpdate && !string.IsNullOrWhiteSpace(series.Title))
            {
                // Manual title: clear every "title source" flag so a later provider refresh
                // can never overwrite it (the refresh guard relies on "no IsTitle source").
                foreach (SeriesProviderEntity sp in dbSeries.Sources)
                    sp.IsTitle = false;
                dbSeries.Title = series.Title;
            }

            // Update provider settings
            List<string> deletedSources = await _providerService.DeleteSourcesIfNeededAsync(series, dbSeries, token)
                .ConfigureAwait(false);
            
            dbSeries = await ConsolidateDBSeriesFromProvidersAsync(dbSeries, dbSeries.Sources.ToList(),
                dbSeries.StoragePath, dbSeries.PauseDownloads, series.StartFromChapter, token);
            
            dbSeries.Sources.CalculateContinueAfterChapter(series.StartFromChapter);
            bool wasPaused = dbSeries.PauseDownloads;
            dbSeries.PauseDownloads = series.PausedDownloads;
            
            // When series gets paused, clear any queued waiting downloads so they're recalculated on resume
            if (series.PausedDownloads && !wasPaused)
            {
                await _jobManagement.ClearWaitingDownloadsForSeriesAsync(series.Id, token)
                    .ConfigureAwait(false);
            }
            
            _db.Series.Update(dbSeries);
            
            await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                dbSeries.Sources, deletedSources, token).ConfigureAwait(false);
            
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            
            await _providerService.RescheduleIfNeededAsync(dbSeries.Sources, true, series.PausedDownloads, token)
                .ConfigureAwait(false);
            
            await _stateService.SyncToRensaioJsonAsync(dbSeries.Id, token).ConfigureAwait(false);
            
            if (existingThumb != dbSeries.ThumbnailUrl)
            {
                await _archiveHelper.WriteComicThumbnailAsync(dbSeries, token).ConfigureAwait(false);
            }

            _logger.LogInformation("Updated series '{title}' (id {SeriesId}): paused={paused}, sources={SourceCount}, path={path}.",
                dbSeries.Title, dbSeries.Id, series.PausedDownloads, dbSeries.Sources.Count, dbSeries.StoragePath);

            return dbSeries.ToSeriesExtendedInfo(settings);
        }

        /// <summary>
        /// Moves the physical storage folder of a series to a new relative location, with rollback.
        /// <para>
        /// The requested <paramref name="requestedPath"/> is a *relative* storage path (e.g.
        /// "Manga/One Piece"). The sequence is:
        /// <list type="number">
        /// <item>Sanitize + validate the target (reject absolute/traversal).</item>
        /// <item>Refuse if a download for this series is currently running.</item>
        /// <item>Rewrite queued download job parameters to the new path.</item>
        /// <item>Move the folder (case-aware two-step for case-insensitive filesystems).</item>
        /// <item>Relocate the hash-cache file to the new relative path.</item>
        /// <item>Delete emptied ancestor directories of the old path (best-effort).</item>
        /// <item>Persist the new <see cref="SeriesEntity.StoragePath"/>.</item>
        /// </list>
        /// Every step is rollbackable; any failure restores the folder, hash cache, DB path and
        /// job parameters before the exception propagates.
        /// </summary>
        /// <param name="dbSeries">The tracked series entity (with Sources loaded).</param>
        /// <param name="requestedPath">The requested relative storage path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentException">When the path is invalid, points at another series, or a download is running.</exception>
        private async Task MoveSeriesStorageAsync(Models.Database.SeriesEntity dbSeries, string requestedPath, CancellationToken token = default)
        {
            if (dbSeries == null)
                return;

            // ── 1. Sanitize + validate the requested relative path ──
            string newRel = SeriesModelExtensions.SanitizeAndValidateStoragePath(requestedPath);
            string oldRel = SeriesModelExtensions.NormalizeStoragePath(dbSeries.StoragePath);

            if (string.Equals(newRel, oldRel, StringComparison.Ordinal))
                return; // no-op (supports case-insensitive compare too: "same" folder)

            // Refuse to move the series onto another series' storage path.
            var otherSeriesUsingTarget = await _db.Series
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id != dbSeries.Id && s.StoragePath == newRel, token)
                .ConfigureAwait(false);
            if (otherSeriesUsingTarget != null)
                throw new ArgumentException($"Target storage path '{newRel}' is already used by another series.");

            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            string oldAbs = Path.Combine(settings.StorageFolder, oldRel);
            string newAbs = Path.Combine(settings.StorageFolder, newRel);
            string newParentAbs = Path.GetDirectoryName(newAbs) ?? string.Empty;

            // ── 2. Guard: no in-flight downloads for this series ──
            // DownloadCommandService serializes per-series via its own KeyedAsyncLock; waiting jobs
            // are rewritten below, but a *running* job would write into the folder mid-move. Block.
            bool runningDownload = await _db.Queues.AnyAsync(q =>
                q.JobType == JobType.Download &&
                q.ExtraKey == dbSeries.Id.ToString() &&
                (q.Status == QueueStatus.Running || q.Status == QueueStatus.Waiting), token).ConfigureAwait(false);
            if (runningDownload)
                throw new ArgumentException("Cannot move the series folder while downloads are queued or running. Pause the series and retry.");

            // ── 3. Rewrite waiting download job parameters (rollback snapshot) ──
            // ChapterDownload is serialized as JobParameters; its StoragePath drives where the
            // finished .cbz is written (DownloadCommandService line: dirPath = Storage/StoragePath).
            var queuedDownloads = await _db.Queues
                .Where(q => q.JobType == JobType.Download && q.ExtraKey == dbSeries.Id.ToString())
                .ToListAsync(token).ConfigureAwait(false);
            Dictionary<Guid, string> originalJobParams = new Dictionary<Guid, string>();
            foreach (EnqueueEntity q in queuedDownloads)
            {
                originalJobParams[q.Id] = q.JobParameters ?? string.Empty;
                if (string.IsNullOrWhiteSpace(q.JobParameters))
                    continue;
                try
                {
                    ChapterDownload? ch = JsonSerializer.Deserialize<ChapterDownload>(q.JobParameters);
                    if (ch == null || string.IsNullOrEmpty(ch.StoragePath))
                        continue;
                    string chNew = ch.StoragePath;
                    // Rewrite both relational forms: exact match of the series' old path and any
                    // descendant (storage path is always relative, single folder per series).
                    if (string.Equals(chNew, oldRel, StringComparison.OrdinalIgnoreCase))
                        chNew = newRel;
                    else if (chNew.StartsWith(oldRel + "/", StringComparison.OrdinalIgnoreCase) ||
                             chNew.StartsWith(oldRel + "\\", StringComparison.OrdinalIgnoreCase))
                        chNew = newRel + chNew[oldRel.Length..];
                    if (string.Equals(chNew, ch.StoragePath, StringComparison.Ordinal))
                        continue;
                    ch.StoragePath = chNew;
                    q.JobParameters = JsonSerializer.Serialize(ch);
                    _db.Touch(q, e => e.JobParameters);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to rewrite download job {JobId} target path during series move; leaving as-is.", q.Id);
                }
            }

            // ── 4. Move the folder (case-aware two-step) ──
            bool movedToTemp = false;
            bool movedToFinal = false;
            string tempAbs = newAbs + "__rensaio_move_tmp__";
            try
            {
                if (!string.Equals(oldAbs, newAbs, StringComparison.OrdinalIgnoreCase) && File.Exists(newAbs))
                    throw new ArgumentException($"Target path '{newRel}' already exists on disk.");

                if (!Directory.Exists(oldAbs))
                {
                    // Folder doesn't exist yet — still record the path and persist, nothing to move.
                    _logger.LogWarning("Series {SeriesId} folder not found on disk at {OldAbs}; recording new path without moving files.", dbSeries.Id, oldAbs);
                }
                else
                {
                    if (!Directory.Exists(newParentAbs))
                        Directory.CreateDirectory(newParentAbs);

                    bool caseOnly = string.Equals(oldAbs, newAbs, StringComparison.OrdinalIgnoreCase);
                    if (caseOnly)
                    {
                        Directory.Move(oldAbs, tempAbs);
                        movedToTemp = true;
                        try
                        {
                            Directory.Move(tempAbs, newAbs);
                            movedToFinal = true;
                        }
                        catch (Exception rollbackSecondLeg)
                        {
                            // Restore the temp leg back to the original name immediately so the
                            // folder is never stranded at the temp path.
                            try { Directory.Move(tempAbs, oldAbs); movedToTemp = false; }
                            catch (Exception rb) { _logger.LogError(rb, "Failed to roll back temp folder {Temp} after failed case-only folder move", tempAbs); }
                            throw rollbackSecondLeg;
                        }
                    }
                    else
                    {
                        Directory.Move(oldAbs, newAbs);
                        movedToFinal = true;
                    }
                }

                // ── 5. Relocate the hash-cache file (old rel path → new rel path) ──
                _hashCache.RelocateSeriesHashCache(oldRel, newRel);

                // ── 6. Delete emptied ancestor dirs of the old path (best-effort) ──
                if (movedToFinal || !Directory.Exists(oldAbs))
                {
                    DeleteEmptyAncestors(oldAbs);
                }

                // ── 7. Persist the new path ──
                dbSeries.StoragePath = newRel;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);

                _logger.LogInformation("Moved series folder {SeriesTitle} (id {SeriesId}): {Old} -> {New}",
                    dbSeries.Title, dbSeries.Id, oldAbs, newAbs);
            }
            catch (Exception ex)
            {
                // ── Rollback ──
                _logger.LogError(ex, "Failed to move series folder {SeriesTitle} (id {SeriesId}): {Old} -> {New}; rolling back.", dbSeries.Title, dbSeries.Id, oldAbs, newAbs);

                // 4a. Folder: reverse the move. If we're mid two-step, restore temp → old; if the
                //     final move completed but a LATER step failed, move new → old.
                try
                {
                    if (movedToTemp && !movedToFinal && Directory.Exists(tempAbs) && !Directory.Exists(oldAbs))
                    {
                        Directory.Move(tempAbs, oldAbs);
                        movedToTemp = false;
                    }
                    else if (movedToFinal && Directory.Exists(newAbs) && !Directory.Exists(oldAbs) &&
                        !string.Equals(newAbs, oldAbs, StringComparison.OrdinalIgnoreCase))
                    {
                        // Case-only: hop back through temp to survive case-insensitive filesystems.
                        if (string.Equals(newAbs, oldAbs, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Move(newAbs, tempAbs);
                            Directory.Move(tempAbs, oldAbs);
                        }
                        else
                        {
                            Directory.Move(newAbs, oldAbs);
                        }
                    }
                }
                catch (Exception rb)
                {
                    _logger.LogError(rb, "Failed to restore series folder to {Old} during rollback", oldAbs);
                }

                // 5a. Hash cache: move back.
                try
                {
                    _hashCache.RelocateSeriesHashCache(newRel, oldRel);
                }
                catch (Exception rb)
                {
                    _logger.LogError(rb, "Failed to restore hash cache for series {SeriesId} during rollback", dbSeries.Id);
                }

                // 3a. Restore original job parameters.
                foreach (var kvp in originalJobParams)
                {
                    var q = queuedDownloads.FirstOrDefault(q => q.Id == kvp.Key);
                    if (q != null)
                    {
                        q.JobParameters = kvp.Value;
                        _db.Touch(q, e => e.JobParameters);
                    }
                }
                if (originalJobParams.Count > 0)
                {
                    try { await _db.SaveChangesAsync(token).ConfigureAwait(false); }
                    catch (Exception se) { _logger.LogError(se, "Failed to persist rollback of job parameters for series {SeriesId}", dbSeries.Id); }
                }

                throw; // propagate the original failure
            }
        }

        /// <summary>
        /// Recursively deletes empty ancestor directories of <paramref name="fullPath"/> (excluding
        /// the storage folder root itself). Best-effort — used to clean up category folders left
        /// empty after a series folder is moved out.
        /// </summary>
        private static void DeleteEmptyAncestors(string fullPath)
        {
            string parent = Path.GetDirectoryName(fullPath) ?? string.Empty;
            while (!string.IsNullOrEmpty(parent) && parent != "." && parent != "/" && parent != "\\")
            {
                try
                {
                    if (!Directory.Exists(parent))
                        break;
                    var entries = Directory.GetFileSystemEntries(parent);
                    if (entries.Length > 0)
                        break; // not empty — stop here
                    Directory.Delete(parent, false);
                }
                catch (Exception)
                {
                    break; // can't delete (locked/permission) — stop ascending
                }
                string next = Path.GetDirectoryName(parent) ?? string.Empty;
                if (string.Equals(next, parent, StringComparison.Ordinal))
                    break;
                parent = next;
            }
        }

        /// <summary>
        /// Deletes a series from the database
        /// </summary>
        /// <param name="id">Series ID to delete</param>
        /// <param name="alsoPhysical">Whether to also delete physical files</param>
        /// <param name="token">Cancellation token</param>
        public async Task DeleteSeriesAsync(Guid id, bool alsoPhysical, CancellationToken token = default)
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (id == Guid.Empty)
            {
                throw new ArgumentException("Invalid Series Guid provided for delete");
            }

            Models.Database.SeriesEntity? dbSeries = await _db.Series.Include(s => s.Sources)
                .FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            if (dbSeries == null)
            {
                _logger.LogWarning("Cannot delete series with ID {SeriesId}: not found.", id);
                throw new KeyNotFoundException($"Series with ID {id} not found");
            }

            _logger.LogInformation("Deleting series '{title}' (id {SeriesId}) alsoPhysical={alsoPhysical}.",
                dbSeries.Title, id, alsoPhysical);

            List<string> deletedSeries = dbSeries.Sources
                .Select(a => a.MihonId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            
            if (alsoPhysical)
                dbSeries.DeletePhysicalSeries(settings, _logger);
            
            foreach (SeriesProviderEntity p in dbSeries.Sources)
            {
                await _providerService.RescheduleIfNeededAsync([p], false, true, token).ConfigureAwait(false);
            }

            // Remove global scrobbling/mapping rows for this series. The model configures
            // ON DELETE CASCADE for SeriesMappings.SeriesId, but mapping rows may exist that
            // are not loaded into this context (so EF's client-side cascade can't see them),
            // and existing databases may still carry the legacy NO ACTION FK. Removing them
            // explicitly makes deletion deterministic regardless of DB/schema state.
            var mappingsToRemove = await _db.SeriesMappings
                .Where(m => m.SeriesId == id)
                .ToListAsync(token).ConfigureAwait(false);
            if (mappingsToRemove.Count > 0)
                _db.SeriesMappings.RemoveRange(mappingsToRemove);

            _db.Series.Remove(dbSeries);
            
            await _providerService.CheckIfTheStorageFlagsChangedTheInLibraryStatusOfLastSeriesAsync(
                [], deletedSeries, token).ConfigureAwait(false);
            
            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            _logger.LogInformation("Deletion of series '{title}' (id {SeriesId}) complete; {Mappings} mappings removed.",
                dbSeries.Title, id, mappingsToRemove.Count);
        }

        


        /// <summary>
        /// Updates a source with latest series information (moved from SeriesUpdateService)
        /// </summary>
        public async Task<JobResult> UpdateSourceAsync(string mihonProviderId, CancellationToken token)
        {
            try
            {
                Dictionary<string, (DateTime, Manga?, ParsedChapter?)> latestDates = await _db.LatestSeries.Where(a => a.MihonProviderId == mihonProviderId).ToDictionaryAsync(a => a.MihonId, a => (a.FetchDate, a.ToManga(), a.Chapters.OrderByDescending(b => b.Index).FirstOrDefault()), token).ConfigureAwait(false);
                ConcurrentDictionary<string, ComboSeries> newChaps = [];
                // Aggregated per-run diagnostics so the caller (and the log reader) can see the
                // outcome of the whole provider refresh in a single line instead of one line per
                // failed title. Purely additive; the individual per-call errors are still logged.
                ConcurrentDictionary<string, int> failureBreakdown = [];
                long fetchedSeries = 0;
                int page = 1;
                bool upToDate = false;
                bool neverDone = latestDates.Count == 0;
                bool fetchNextPage;
                ISourceInterop src;
                try
                {
                    src = await _mihon.SourceFromProviderIdAsync(mihonProviderId, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to get Latest Series from {mihonProviderId}", mihonProviderId);
                    return JobResult.Failed;
                }
                string provider = src.Name + " (" + src.Language + ")";
                _logger.LogInformation("Updating Latest Series from Provider {provider}...", provider);
                do
                {
                    MangaList? res;
                    res = await _mihon.MihonErrorWrapperAsync(
                        () => src.GetLatestAsync(page, token),
                        "Unable to get Latest Series from {provider}", provider).ConfigureAwait(false);
                    if (res==null)
                        return JobResult.Failed;

                    SettingsDto s = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                    // Cap concurrency: each details/chapters fetch crosses the IKVM boundary and
                    // consumes process thread budget shared with CEF (see SourceTimeoutGate).
                    var latestMaxConcurrency = Math.Min(s.NumberOfSimultaneousDownloadsPerProvider, 4);
                    // Dedupe by manga URL: some sources (e.g. Madara) return one row per updated
                    // chapter, so the same manga can appear multiple times on a "latest" page.
                    // Fetching the same manga concurrently trips the extension's own guard
                    // ("getMangaUpdate must not be called concurrently for same manga").
                    var uniqueMangas = res.Mangas
                        .GroupBy(ss => string.IsNullOrEmpty(ss.Url) ? ss.Title : ss.Url, StringComparer.Ordinal)
                        .Select(g => g.First())
                        .ToList();
                    await Parallel.ForEachAsync(uniqueMangas, new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = latestMaxConcurrency
                    },
                        async (ss, b) =>
                        {
                            if (upToDate)
                                return;
                            ComboSeries combo = new ComboSeries();
                            string mihonId = mihonProviderId + "|" + ss.Url;
                            combo.MihonId = mihonId;
                            // Serialize per (provider+manga) across the whole process so a library
                            // GetChapters job can never race this loop on the same manga. Key uses
                            // the canonical numeric source id + url so every caller of the same
                            // source collapses onto the same lock (see ISourceInterop.Id).
                            string mangaLockKey = src.Id + "|" + ss.Url;
                            if (!latestDates.TryGetValue(mihonId, out (DateTime, Manga?, ParsedChapter?) value) ||
                                (value.Item1.AddDays(7) < DateTime.UtcNow))
                            {
                                MangaUpdate? update = await _mihon.MihonErrorWrapperLockedAsync(
                                    () => src.GetDetailsAndChaptersAsync(ss, token),
                                    "Unable to get Series {Title} from {provider}", mangaLockKey, failureBreakdown, ss.Title, provider).ConfigureAwait(false);
                                if (update == null)
                                    return;
                                fetchedSeries++;
                                combo.Series = update.Manga;
                                combo.Chapters = update.Chapters;
                                newChaps[mihonId] = combo;
                            }
                            else
                            {
                                // Fresh entry already in DB; still fetch chapter list to detect
                                // "up to date" and record the latest chapter, but also guard the
                                // same-manga lock to avoid racing a library GetChapters job.
                                List<ParsedChapter>? chaps = await _mihon.MihonErrorWrapperLockedAsync(
                                    () => src.GetChaptersAsync(ss, token),
                                    "Unable to get Series {Title} Chapters from {provider}", mangaLockKey, failureBreakdown, ss.Title, provider).ConfigureAwait(false);
                                if (chaps == null)
                                {
                                    newChaps.Remove(mihonId, out _);
                                    return;
                                }
                                fetchedSeries++;
                                combo.Chapters = chaps;
                                newChaps[mihonId] = combo;
                            }

                            ParsedChapter? latest_online = combo.Chapters.OrderByDescending(a => a.Index).FirstOrDefault();
                            if (latest_online != null && latestDates.TryGetValue(mihonId, out (DateTime, Manga?, ParsedChapter?) value2) && value2.Item2 != null && value2.Item3!=null)
                            {
                                if ((latestDates[mihonId].Item3!.Index >= latest_online.Index) &&
                                    (latestDates[mihonId].Item3!.DateUpload >= latest_online.DateUpload))
                                {
                                    upToDate = true;
                                }
                            }
                        }).ConfigureAwait(false);
                    if (upToDate)
                        break;
                    // Without an end condition of its own this loop only stops on a series it
                    // already knows. When the stored state is stale that never happens, and the
                    // run walks the whole catalog while nothing gets saved (saving is below).
                    fetchNextPage = LatestPagingPolicy.ShouldFetchNextPage(page, res.Mangas.Count, res.HasNextPage, upToDate, neverDone);
                    page++;
                } while (fetchNextPage);

                List<string> ids = newChaps.Keys.ToList();
                List<LatestSerieEntity> toUpdate = await _db.LatestSeries.Where(a => ids.Contains(a.MihonId)).ToListAsync(token).ConfigureAwait(false);
                List<(LatestSerieEntity, SeriesProviderEntity)> toCheck = [];

                foreach (ComboSeries c in newChaps.Values)
                {
                    LatestSerieEntity? s = toUpdate.FirstOrDefault(a => a.MihonId == c.MihonId);
                    if (s == null)
                    {
                        s = new LatestSerieEntity();
                        s.MihonId = c.MihonId;
                        s.MihonProviderId = mihonProviderId;
                        _db.LatestSeries.Add(s);
                    }
                    if (c.Series != null)
                    {
                        await s.PopulateSeriesAsync(src, c.Series, _cache).ConfigureAwait(false);
                    }
                    s.Chapters = c.Chapters;
                    ParsedChapter? latest_online = s.Chapters.OrderByDescending(a => a.Index).FirstOrDefault();
                    DateTime latestUTC = latest_online?.DateUpload.DateTime ?? DateTime.MinValue;

                    if (latestUTC > DateTime.UtcNow || latestUTC.AddMonths(1) < DateTime.UtcNow)
                    {
                        latestUTC = DateTime.UtcNow;
                    }
                    s.FetchDate = latestUTC;
                    s.LatestChapter = latest_online?.ParsedNumber ?? -1.0m;
                    s.ChapterCount = s.Chapters.Count;
                    s.LatestChapterTitle = latest_online?.Name ?? "";
                    SeriesProviderEntity? serie = await _db.SeriesProviders
                        .Where(a => a.MihonId == s.MihonId).AsNoTracking()
                        .FirstOrDefaultAsync(token).ConfigureAwait(false);
                    s.InLibrary = InLibraryStatus.NotInLibrary;
                    if (serie != null)
                    {
                        s.SeriesId = serie.SeriesId;
                        if (serie.IsDisabled || serie.IsUninstalled)
                            s.InLibrary = InLibraryStatus.InLibraryButDisabled;
                        else
                        {
                            toCheck.Add((s, serie));
                            s.InLibrary = InLibraryStatus.InLibrary;
                        }
                    }
                }
                await _db.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var u in toCheck)
                {
                    Models.Database.SeriesEntity series = await _db.Series.Include(a => a.Sources)
                        .Where(a => a.Id == u.Item2.SeriesId).AsNoTracking().FirstAsync(token).ConfigureAwait(false);
                    if (!series.PauseDownloads)
                    {
                        List<ChapterDownload> chaps = series.GenerateDownloadsFromChapterData(u.Item2, u.Item1.Chapters);
                        if (chaps.Count > 0)
                        {
                            await _downloadCommand.QueueChapterDownloadsAsync(u.Item2, chaps, token).ConfigureAwait(false);
                        }
                    }
                }
                _logger.LogInformation(
                    "Latest Series update from Provider {provider} complete. Pages scanned: {pages}, series fetched: {fetched}, failed: {failed}, up to date: {upToDate}",
                    provider, page - 1, fetchedSeries, failureBreakdown.Count, upToDate);

                // One aggregated line for the run's failures (if any) so the failure picture
                // survives triage without scrolling through every per-title error above.
                if (failureBreakdown.Count > 0)
                {
                    _logger.LogWarning(
                        "Latest Series update from Provider {provider} completed with {count} failures: {breakdown}.",
                        provider, failureBreakdown.Count,
                        string.Join(", ", failureBreakdown.Select(kv => $"{kv.Key}: {kv.Value}")));
                }

                return JobResult.Success;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error Updating Source : {Message}", e.Message);
                return JobResult.Failed;
            }
        }


        /// <summary>
        /// Downloads/updates a specific series provider (moved from SeriesUpdateService)
        /// </summary>
        public async Task<JobResult> GetChaptersAsync(Guid seriesProvider, CancellationToken token = default)
        {
            // Load TRACKING entities (no AsNoTracking) so we can save error/refresh data
            SeriesProviderEntity? serie = await _db.SeriesProviders.FirstOrDefaultAsync(s => s.Id == seriesProvider, token).ConfigureAwait(false);
            if (serie == null)
            {
                _logger.LogWarning("Series Provider {SeriesProvider} no longer exists", seriesProvider);
                return JobResult.Delete;
            }
            if (serie.IsDisabled || serie.IsUninstalled)
            {
                _logger.LogWarning("Series Provider {SeriesProvider} is disabled or uninstalled", seriesProvider);
                return JobResult.Failed;
            }
            if (string.IsNullOrEmpty(serie.MihonProviderId))
            {
                _logger.LogWarning("Series Provider {SeriesProvider} has no longer valid Mihon Id; deleting job", seriesProvider);
                return JobResult.Delete;
            }

            Models.Database.SeriesEntity? series = await _db.Series.Include(a => a.Sources)
                .FirstOrDefaultAsync(s => s.Id == serie.SeriesId, token).ConfigureAwait(false);
            if (series == null)
            {
                _logger.LogWarning("Series {Title} for Provider {SeriesProvider} not found", serie.Title, seriesProvider);
                return JobResult.Delete;
            }

            ISourceInterop src;
            try
            {
                src = await _mihon.SourceFromProviderIdAsync(serie.MihonProviderId!, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to get Chapter from {mihonProviderId}", serie.MihonProviderId);
                // Track the error on the provider
                serie.LastErrorDate = DateTime.UtcNow;
                serie.ConsecutiveErrorCount++;
                _db.Touch(serie, a => a.LastErrorDate);
                _db.Touch(serie, a => a.ConsecutiveErrorCount);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return JobResult.Failed;
            }
            
            string provider = src.Name + " (" + src.Language + ")";
            _logger.LogInformation("Getting chapters from Series {series} Provider {provider}", serie.Title, provider);
            // Serialize per (provider+manga) so this refresh cannot race the provider-wide
            // latest loop (or another job) on the same manga — Madara sources throw
            // "getMangaUpdate must not be called concurrently for same manga" on collision.
            // Key uses the canonical source id (see ISourceInterop.Id) so all callers of the
            // same source collapse onto one lock.
            Manga manga = serie.ToManga()!;
            string mangaLockKey = src.Id + "|" + manga.Url;
            List<ParsedChapter>? chapterData;
            try
            {
                chapterData = await _mihon.MihonErrorWrapperLockedAsync(
                    () => src.GetChaptersAsync(manga, token),
                    "Unable to get Chapters from {series} from {provider}", mangaLockKey, serie.Title, provider).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Track the error on the provider
                serie.LastErrorDate = DateTime.UtcNow;
                serie.ConsecutiveErrorCount++;
                _db.Touch(serie, a => a.LastErrorDate);
                _db.Touch(serie, a => a.ConsecutiveErrorCount);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return JobResult.Failed;
            }

            if (chapterData == null || chapterData.Count == 0)
            {
                _logger.LogWarning("Series {series} from Provider {provider} has no chapters.", serie.Title, provider);
                // If chapters returned empty, it might still be a valid state but we shouldn't flag as error
                // Only track as error if it was a connection/parsing failure, not empty results
                return JobResult.Failed;
            }

            _logger.LogInformation("Fetched {count} chapters for Series {series} from Provider {provider}.",
                chapterData.Count, serie.Title, provider);

            // Success — reset error tracking
            serie.ConsecutiveErrorCount = 0;
            serie.LastSuccessfulFetchDate = DateTime.UtcNow;
            _db.Touch(serie, a => a.ConsecutiveErrorCount);
            _db.Touch(serie, a => a.LastSuccessfulFetchDate);

            // Refresh series metadata (status, description, etc.) from the extension
            try
            {
                var extensionManga = await _mihon.MihonErrorWrapperLockedAsync(
                    () => src.GetDetailsAsync(manga, token),
                    "Unable to get Details from {series} from {provider}", mangaLockKey, serie.Title, provider).ConfigureAwait(false);

                if (extensionManga != null)
                {
                    SeriesStatus newStatus = (SeriesStatus)(int)extensionManga.Status;
                    bool statusChanged = newStatus != serie.LastKnownStatus;

                    // Update the series-level metadata.
                    // Only the provider flagged as the title source may overwrite the canonical
                    // series title. When NO provider is flagged as the title source the title is
                    // a *manual* title (set via "Edit title" on the series page) and must never
                    // be clobbered by a background refresh.
                    if (!string.IsNullOrEmpty(extensionManga.Title) && serie.IsTitle)
                        series.Title = extensionManga.Title;
                    if (!string.IsNullOrEmpty(extensionManga.Artist))
                        series.Artist = extensionManga.Artist;
                    if (!string.IsNullOrEmpty(extensionManga.Author))
                        series.Author = extensionManga.Author;
                    if (!string.IsNullOrEmpty(extensionManga.Description))
                        series.Description = extensionManga.Description;
                    if (!string.IsNullOrEmpty(extensionManga.Genre))
                        series.Genre = extensionManga.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    series.Status = newStatus;

                    // Update provider-level metadata
                    serie.Status = newStatus;
                    serie.LastKnownStatus = newStatus;
                    serie.LastSeriesInfoRefreshDate = DateTime.UtcNow;
                    _db.Touch(serie, a => a.Status);
                    _db.Touch(serie, a => a.LastKnownStatus);
                    _db.Touch(serie, a => a.LastSeriesInfoRefreshDate);

                    // If the series was completed/cancelled/hiatus, clear any active health alert
                    if (newStatus == SeriesStatus.COMPLETED ||
                        newStatus == SeriesStatus.CANCELLED ||
                        newStatus == SeriesStatus.ON_HIATUS ||
                        newStatus == SeriesStatus.PUBLISHING_FINISHED)
                    {
                        var existingAlert = await _db.HealthStatuses
                            .FirstOrDefaultAsync(h => h.TargetType == HealthStatusTargetType.Series
                                && h.TargetId == series.Id && h.IsActive, token).ConfigureAwait(false);
                        if (existingAlert != null)
                        {
                            existingAlert.IsActive = false;
                            existingAlert.ResolvedAt = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Metadata refresh is best-effort; don't fail the entire chapter fetch
                _logger.LogWarning(ex, "Failed to refresh metadata for {series} from {provider}", serie.Title, provider);
            }

            // Update LastChapterDate on the series entity
            if (chapterData.Count > 0)
            {
                DateTime? latestChapterDate = chapterData
                    .Where(c => c.DateUpload != default)
                    .Max(c => c.DateUpload.DateTime);

                if (latestChapterDate.HasValue)
                {
                    if (series.LastChapterDate == null || latestChapterDate > series.LastChapterDate)
                    {
                        series.LastChapterDate = latestChapterDate;
                    }
                }
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);

            // Recalculate release cadence after fetching new chapters
            await _cadenceService.RecalculateCadenceAsync(series.Id, token).ConfigureAwait(false);

            // Sync rensaio.json after metadata refresh (series.Title, Artist, etc. may have changed)
            await _stateService.SyncToRensaioJsonAsync(series.Id, token).ConfigureAwait(false);

            // Respect the series-level pause as the source of truth for downloads.
            // Metadata above is always refreshed (status drives alerts), but no chapters are
            // queued while paused. Pause is normally enforced by disabling the recurring job,
            // however some paths re-run this job with the job enabled (e.g. extension
            // (re)install/update reschedules providers with forceDisable=false), which would
            // otherwise bypass the pause flag — guarding here closes every such path.
            if (series.PauseDownloads)
            {
                _logger.LogInformation(
                    "GetChapters refresh of Series {series} from Provider {provider} complete: {chapterCount} chapters fetched (status {status}), downloads skipped (paused).",
                    serie.Title, provider, chapterData.Count, serie.LastKnownStatus);
                _logger.LogInformation("Series {series} is paused; metadata refreshed but skipping chapter downloads", serie.Title);
                return JobResult.Success;
            }

            List<ChapterDownload> chaps = series.GenerateDownloadsFromChapterData(serie, chapterData);
            int newCount = chaps.Count(a => !a.IsUpdate);
            int updateCount = chaps.Count(a => a.IsUpdate);

            JobResult result = await _downloadCommand.QueueChapterDownloadsAsync(serie, chaps, token).ConfigureAwait(false);
            if (chaps.Count > 0)
            {
                _logger.LogInformation(
                    "GetChapters refresh of Series {series} from Provider {provider} complete: {chapterCount} chapters fetched (status {status}), queued {newCount} new and {updateCount} updated chapter download(s).",
                    serie.Title, provider, chapterData.Count, serie.LastKnownStatus, newCount, updateCount);
            }
            else
            {
                _logger.LogInformation(
                    "GetChapters refresh of Series {series} from Provider {provider} complete: {chapterCount} chapters fetched (status {status}), no new chapters to download.",
                    serie.Title, provider, chapterData.Count, serie.LastKnownStatus);
            }

            return result;
        }

        /// <summary>
        /// Triggers an immediate refresh for a single series: re-fetches metadata (status, title,
        /// cover, description, etc.) and checks for new chapters by enqueuing the GetChapters job
        /// for each active provider. Honors the series pause flag (paused series refresh metadata
        /// but do not download — enforced inside <see cref="GetChaptersAsync"/>).
        /// </summary>
        /// <param name="seriesId">The series to refresh.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of providers queued for refresh.</returns>
        public async Task<int> RefreshSeriesMetadataAsync(Guid seriesId, CancellationToken token = default)
        {
            var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(a=>a.Id==seriesId);
            string name = series?.Title ?? seriesId.ToString();
            List<SeriesProviderEntity> providers = await _db.SeriesProviders
                .Where(p => p.SeriesId == seriesId && !p.IsUnknown && !p.IsLocal
                    && !p.IsDisabled && !p.IsUninstalled)
                .ToListAsync(token).ConfigureAwait(false);

            int queued = 0;
            foreach (SeriesProviderEntity p in providers)
            {
                if (string.IsNullOrEmpty(p.MihonProviderId))
                    continue;
                await _jobManagement.EnqueueJobAsync(JobType.GetChapters, p.Id, Priority.High,
                    key: p.Id.ToString(), token: token).ConfigureAwait(false);
                queued++;
            }

            _logger.LogInformation("Queued metadata refresh for {count} provider(s) of series {name}",
                queued, name);
            return queued;
        }

        /// <summary>
        /// Re-downloads (or downloads) a single chapter, replacing any existing file on disk. The
        /// target source is resolved by priority — the storage source that offers the chapter, then
        /// the source currently holding the file, then any other remote-capable source — unless an
        /// explicit <paramref name="providerId"/> override is supplied. Honors the series pause flag
        /// (paused series cannot re-download). Bypasses the bulk "already downloaded" filters so an
        /// existing chapter is genuinely re-fetched.
        /// </summary>
        /// <param name="seriesId">The series owning the chapter.</param>
        /// <param name="chapterNumber">The chapter number to (re-)download.</param>
        /// <param name="providerId">Optional explicit source to force; null = priority default.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task<RedownloadResult> RedownloadChapterAsync(Guid seriesId, decimal chapterNumber,
            Guid? providerId = null, CancellationToken token = default)
        {
            Models.Database.SeriesEntity? series = await _db.Series.Include(a => a.Sources)
                .FirstOrDefaultAsync(s => s.Id == seriesId, token).ConfigureAwait(false);
            if (series == null)
            {
                _logger.LogWarning("Redownload of chapter {Chapter} of series {SeriesId}: series not found.",
                    chapterNumber, seriesId);
                return new RedownloadResult(RedownloadOutcome.SeriesNotFound);
            }

            // Pause is authoritative — block explicit re-downloads while the series is paused.
            if (series.PauseDownloads)
            {
                _logger.LogWarning("Redownload of chapter {Chapter} of series '{title}': series is paused.",
                    chapterNumber, series.Title);
                return new RedownloadResult(RedownloadOutcome.Paused);
            }

            bool HasChapter(SeriesProviderEntity p) =>
                p.Chapters.Any(c => !c.IsDeleted && c.Number == chapterNumber);
            bool Capable(SeriesProviderEntity p) =>
                !p.IsUnknown && !p.IsLocal && !p.IsDisabled && !p.IsUninstalled
                && !string.IsNullOrEmpty(p.MihonProviderId);

            SeriesProviderEntity? target;
            if (providerId.HasValue)
            {
                target = series.Sources.FirstOrDefault(p => p.Id == providerId.Value);
                if (target == null || !Capable(target))
                {
                    _logger.LogWarning("Redownload of chapter {Chapter} of series '{title}': no capable source available.",
                        chapterNumber, series.Title);
                    return new RedownloadResult(RedownloadOutcome.NoSourceAvailable);
                }
            }
            else
            {
                List<SeriesProviderEntity> candidates = series.Sources
                    .Where(p => Capable(p) && HasChapter(p)).ToList();
                target = candidates.FirstOrDefault(p => p.IsStorage)
                    ?? candidates.FirstOrDefault(p => p.Chapters.Any(c => c.Number == chapterNumber && !string.IsNullOrEmpty(c.Filename)))
                    ?? candidates.FirstOrDefault();
                if (target == null)
                {
                    _logger.LogWarning("Redownload of chapter {Chapter} of series '{title}': no source offers the chapter.",
                        chapterNumber, series.Title);
                    return new RedownloadResult(RedownloadOutcome.NoSourceAvailable);
                }
            }

            // Re-fetch the live chapter list so the download uses a fresh URL.
            ISourceInterop src;
            try
            {
                src = await _mihon.SourceFromProviderIdAsync(target.MihonProviderId!, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to resolve source for provider {Provider}", target.Provider);
                return new RedownloadResult(RedownloadOutcome.NoSourceAvailable);
            }

            Manga targetManga = target.ToManga()!;
            string targetLockKey = src.Id + "|" + targetManga.Url;
            List<ParsedChapter>? chapterData = await _mihon.MihonErrorWrapperLockedAsync(
                () => src.GetChaptersAsync(targetManga, token),
                "Unable to get Chapters from {series} from {provider}", targetLockKey, series.Title, target.Provider).ConfigureAwait(false);
            if (chapterData == null || chapterData.Count == 0)
            {
                _logger.LogWarning("Redownload of chapter {Chapter} of series '{title}': chapter not found in live chapter list.",
                    chapterNumber, series.Title);
                return new RedownloadResult(RedownloadOutcome.ChapterNotFound);
            }

            // Apply the same scanlator scoping the bulk download path uses.
            chapterData.ForEach(a =>
            {
                if (string.IsNullOrEmpty(a.Scanlator))
                    a.Scanlator = target.Provider;
            });
            IEnumerable<ParsedChapter> pool = chapterData;
            if (target.Scanlator == target.Provider || string.IsNullOrEmpty(target.Scanlator))
                pool = pool.Where(a => string.IsNullOrEmpty(a.Scanlator) || a.Scanlator == target.Provider);
            else
                pool = pool.Where(a => a.Scanlator == target.Scanlator);

            ParsedChapter? match = pool.FirstOrDefault(c => c.ParsedNumber == chapterNumber);
            if (match == null)
            {
                _logger.LogWarning("Redownload of chapter {Chapter} of series '{title}': chapter not found after scanlator scoping.",
                    chapterNumber, series.Title);
                return new RedownloadResult(RedownloadOutcome.ChapterNotFound);
            }

            // Remove any existing on-disk copy of this chapter (held by whichever source) and reset
            // its row, so the fresh download replaces it instead of leaving an orphan or duplicate.
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            bool cleared = false;
            foreach (SeriesProviderEntity sp in series.Sources)
            {
                foreach (Models.Chapter c in sp.Chapters.Where(c => c.Number == chapterNumber && !string.IsNullOrEmpty(c.Filename)))
                {
                    _hashCache.DeleteChapterHash(series.StoragePath, c.Filename!);
                    string full = Path.Combine(settings.StorageFolder, series.StoragePath, c.Filename!);
                    if (File.Exists(full))
                    {
                        try { File.Delete(full); }
                        catch (Exception e) { _logger.LogWarning(e, "Unable to delete file {full} for re-download", full); }
                    }
                    c.Filename = null;
                    c.DownloadDate = null;
                    c.ShouldDownload = true;
                    _db.Touch(sp, a => a.Chapters);
                    cleared = true;
                }
            }
            if (cleared)
            {
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                await _stateService.SyncToRensaioJsonAsync(series.Id, token).ConfigureAwait(false);
            }

            // Build a single targeted download, bypassing the bulk "already downloaded" filters.
            List<ChapterDownload> chaps = series.ToDownloads(target, new List<ParsedChapter> { match }, series.StoragePath);
            await _downloadCommand.QueueChapterDownloadsAsync(target, chaps, token).ConfigureAwait(false);

            _logger.LogInformation("Queued re-download of chapter {Chapter} for series {Series} from {Provider}",
                chapterNumber, series.Title, target.Provider);
            return new RedownloadResult(RedownloadOutcome.Queued, target.Provider, chaps.Count);
        }
       // Private helper methods
        private async Task<Models.Database.SeriesEntity?> FindExistingSeriesAsync(AugmentedResponseDto ProviderSeriesDetails,
            SettingsDto settings, Dictionary<string, Guid> paths, CancellationToken token)
        {
            if (ProviderSeriesDetails.StorageFolderPath.StartsWith(settings.StorageFolder))
                ProviderSeriesDetails.StorageFolderPath = ProviderSeriesDetails.StorageFolderPath[settings.StorageFolder.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            ProviderSeriesDetails.StorageFolderPath = settings.StorageFolder.GetActualDirectoryPathCaseInsensitive(
                ProviderSeriesDetails.StorageFolderPath);

            if (paths.TryGetValue(ProviderSeriesDetails.StorageFolderPath, out Guid id))
            {
                return await _db.Series.FirstOrDefaultAsync(s => s.Id == id, token).ConfigureAwait(false);
            }

            // Search by title similarity
            var allProvs = await _db.SeriesProviders.Select(a => new { a.Title, a.SeriesId })
                .ToListAsync(token).ConfigureAwait(false);
            
            foreach (var n in allProvs)
            {
                foreach (var ser in ProviderSeriesDetails.Series)
                {
                    if (n.Title.AreStringSimilar(ser.Title, 0))
                    {
                        return await _db.Series.FirstOrDefaultAsync(a => a.Id == n.SeriesId, token)
                            .ConfigureAwait(false);
                    }
                }
            }

            return null;
        }

        private async Task<List<SeriesProviderEntity>> ProcessSeriesProvidersAsync(AugmentedResponseDto ProviderSeriesDetails, List<SeriesProviderEntity> existingProviders, CancellationToken token = default)
        {
            List<ImportProviderSnapshot> pInfos = ProviderSeriesDetails.LocalInfo?.Providers ?? [];

            foreach (var fs in ProviderSeriesDetails.Series)
            {
                ImportProviderSnapshot? pInfo = FindMatchingImportProviderSnapshot(pInfos, fs);
                if (pInfo != null)
                    pInfos.Remove(pInfo);

                var existingProvider = existingProviders.FirstOrDefault(sp => sp.IsMatchingProvider(fs));
                if (existingProvider != null)
                {
                    string provider = fs.Provider;
                    if (!string.IsNullOrEmpty(fs.Scanlator))
                        provider += "-" + fs.Scanlator;
                    
                    _logger.LogInformation("Found existing Provider for '{Title}': {Lang}/{provider}.",
                        fs.Title, fs.Lang, provider);
                    
                    await InternalCreateOrUpdateProviderFromProviderSeriesDetailsAsync(fs, existingProvider, token).ConfigureAwait(false);
                }
                else
                {
                    existingProvider = await InternalCreateOrUpdateProviderFromProviderSeriesDetailsAsync(fs,null, token).ConfigureAwait(false);
                    _db.SeriesProviders.Add(existingProvider);
                    existingProviders.Add(existingProvider);
                }

                if (pInfo != null)
                {
                    InternalAssignArchives(existingProvider, pInfo.Archives);
                    _db.Touch(existingProvider, a => a.Chapters);
                }
            }

            // Add remaining provider infos — check for existing providers first to ensure idempotency
            foreach (ImportProviderSnapshot p in pInfos)
            {
                // Try to find an existing provider by matching provider name + language + scanlator
                var existingProvider = existingProviders.FirstOrDefault(sp =>
                    sp.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                    sp.Language.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase) &&
                    (string.IsNullOrEmpty(p.Scanlator) ||
                     sp.Scanlator.Equals(p.Scanlator, StringComparison.InvariantCultureIgnoreCase)));

                if (existingProvider != null)
                {
                    // Provider already exists — just update chapters
                    _logger.LogInformation("Found existing provider '{Provider}' for remaining provider info. Updating chapters.",
                        p.Provider);
                    InternalAssignArchives(existingProvider, p.Archives);
                }
                else
                {
                    var nProvider = p.ToSeriesProvider();
                    InternalAssignArchives(nProvider, p.Archives);
                    _db.SeriesProviders.Add(nProvider);
                    existingProviders.Add(nProvider);
                }
            }

            return existingProviders;
        }

        private static ImportProviderSnapshot? FindMatchingImportProviderSnapshot(List<ImportProviderSnapshot> pInfos, ProviderSeriesDetails fs)
        {
            foreach (ImportProviderSnapshot p in pInfos)
            {
                if (string.IsNullOrEmpty(p.Scanlator))
                {
                    if (fs.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                        fs.Lang.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return p;
                    }
                }
                else
                {
                    if (fs.Provider.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                        (fs.Scanlator.Equals(p.Scanlator, StringComparison.InvariantCultureIgnoreCase) ||
                         fs.Scanlator.Equals(p.Provider, StringComparison.InvariantCultureIgnoreCase)) &&
                        fs.Lang.Equals(p.Language, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return p;
                    }
                }
            }
            return null;
        }

        private static void UpdateProviderSettings(SeriesExtendedDto series, Models.Database.SeriesEntity dbSeries)
        {
            SeriesProviderEntity? newTitle = null;
            SeriesProviderEntity? newCover = null;
            foreach (ProviderExtendedDto p in series.Providers)
            {
                SeriesProviderEntity? n = dbSeries.Sources.FirstOrDefault(a => a.Id == p.Id);
                if (n == null)
                    continue;

                n.IsDisabled = p.IsDisabled;
                n.IsStorage = p.IsStorage;
                if (p.UseTitle && !n.IsTitle)
                    newTitle = n;
                if (p.UseCover && !n.IsCover)
                    newCover = n;
                n.IsTitle = p.UseTitle;
                n.IsCover = p.UseCover;
                n.IsLocal = p.IsLocal;
                n.ContinueAfterChapter = p.ContinueAfterChapter;
            }

            // Title/cover selection is exclusive. A stale or partial DTO can leave several
            // sources flagged at once, and consolidation would then keep whichever source
            // enumerates first instead of the user's pick. Keep only one: the source that
            // just turned on, or for pre-existing duplicates the one matching the current
            // series value.
            List<SeriesProviderEntity> titled = dbSeries.Sources.Where(a => a.IsTitle).ToList();
            if (titled.Count > 1)
            {
                SeriesProviderEntity keep = newTitle
                    ?? titled.FirstOrDefault(a => a.Title == dbSeries.Title)
                    ?? titled[0];
                foreach (SeriesProviderEntity sp in titled)
                    sp.IsTitle = sp == keep;
            }

            List<SeriesProviderEntity> covered = dbSeries.Sources.Where(a => a.IsCover).ToList();
            if (covered.Count > 1)
            {
                SeriesProviderEntity keep = newCover
                    ?? covered.FirstOrDefault(a => a.ThumbnailUrl == dbSeries.ThumbnailUrl)
                    ?? covered[0];
                foreach (SeriesProviderEntity sp in covered)
                    sp.IsCover = sp == keep;
            }
        }

        private void InternalAssignArchives(SeriesProviderEntity provider, List<ProviderArchiveSnapshot>? archives)
        {
            provider.AssignArchives(archives);
            _db.Touch(provider, e => e.Chapters);
        }

        private async Task<SeriesProviderEntity> InternalCreateOrUpdateProviderFromProviderSeriesDetailsAsync(ProviderSeriesDetails fs, SeriesProviderEntity? provider = null, CancellationToken token = default)
        {
            provider = await fs.CreateOrUpdateAsync(_cache, provider, token).ConfigureAwait(false);
            _db.Touch(provider, e => e.Chapters);
            return provider;
        }

        /// <summary>
        /// Syncs ExternalMappings from an ImportSeriesSnapshot (e.g., from rensaio.json)
        /// into the SeriesMappings table. Used by both:
        /// - Setup Wizard (with UserLevel.Owner)
        /// - Import Series Wizard (with the logged-in user's level)
        /// </summary>
        /// <param name="seriesId">The ID of the series to upsert mappings for.</param>
        /// <param name="localInfo">The snapshot containing ExternalMappings.</param>
        /// <param name="userId">The user ID to associate with the mappings (Guid.Empty for setup wizard).</param>
        /// <param name="userLevel">The user level for role-based overwrite protection.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task SyncExternalMappingsFromSnapshotAsync(
            Guid seriesId,
          
            ImportSeriesSnapshot localInfo,
            Guid userId,
            UserLevel userLevel,
            CancellationToken token = default)
        {
            var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(a => a.Id == seriesId);
            string title = series?.Title ?? seriesId.ToString();

            var mappings = localInfo?.Series.ExternalMappings;
            if (mappings == null || mappings.Count == 0)
                return;

            foreach (var mapping in mappings)
            {
                if (string.IsNullOrEmpty(mapping.Provider) || string.IsNullOrEmpty(mapping.ExternalId))
                    continue;

                if (!Enum.TryParse<ExternalSeriesProvider>(mapping.Provider, out var provider))
                {
                    _logger.LogWarning("Unknown scrobbler provider '{Provider}' in ExternalMappings for series {title}",
                        mapping.Provider, title);
                    continue;
                }

                var existing = await _db.SeriesMappings
                    .FirstOrDefaultAsync(m => m.SeriesId == seriesId && m.Provider == provider, token)
                    .ConfigureAwait(false);

                if (existing != null)
                {
                    // Only overwrite if the new user's level >= existing user's level
                    if (userLevel >= existing.UserRole)
                    {
                        existing.ExternalSeriesId = mapping.ExternalId;
                        existing.ExternalSeriesTitle = mapping.ExternalTitle;
                        existing.UserUid = userId;
                        existing.UserRole = userLevel;
                        existing.UpdateDate = DateTime.UtcNow;
                    }
                }
                else
                {
                    _db.SeriesMappings.Add(new SeriesMappingEntity
                    {
                        Id = Guid.NewGuid(),
                        SeriesId = seriesId,
                        Provider = provider,
                        ExternalSeriesId = mapping.ExternalId,
                        ExternalSeriesTitle = mapping.ExternalTitle,
                        UserUid = userId,
                        UserRole = userLevel,
                        UpdateDate = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _logger.LogDebug("Synced {Count} ExternalMappings to SeriesMappings for series {title}",
                mappings.Count, title);
        }

        private async Task<Models.Database.SeriesEntity> ConsolidateDBSeriesFromProvidersAsync(Models.Database.SeriesEntity? dbSeries,
            List<SeriesProviderEntity> providers, string path, bool startDisabled, decimal? startFromChapter, CancellationToken token = default)
        {
            var consolidatedSeries = providers.ToProviderSeriesDetails();
            
            if (dbSeries != null)
            {
                dbSeries.FillSeriesFromProviderSeriesDetails(consolidatedSeries, startFromChapter);
            }
            else
            {
                dbSeries = consolidatedSeries.ToSeries(path);
                dbSeries.PauseDownloads = startDisabled;
                dbSeries.StartFromChapter = startFromChapter;
                await _db.Series.AddAsync(dbSeries, token).ConfigureAwait(false);
            }

            // Derive the series Type (genre → categorized path → Unknown) when it is still empty.
            // Matching is case-invariant and the result is PascalCased.
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            dbSeries.EnsureSeriesType(settings.CategorizedFolders, settings.Categories);

            return dbSeries;
        }

        /// <summary>
        /// Updates all SeriesMappings that were created with UserUid == Guid.Empty (setup wizard)
        /// to the actual owner user ID. Called after the owner is chosen/created in the setup wizard.
        /// </summary>
        /// <param name="ownerId">The actual owner user ID.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task UpdateSeriesMappingsOwnerAsync(Guid ownerId, CancellationToken token = default)
        {
            var orphanMappings = await _db.SeriesMappings
                .Where(m => m.UserUid == Guid.Empty)
                .ToListAsync(token)
                .ConfigureAwait(false);

            if (orphanMappings.Count == 0)
                return;

            foreach (var mapping in orphanMappings)
            {
                mapping.UserUid = ownerId;
            }

            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _logger.LogDebug("Updated {Count} SeriesMappings UserUid from Guid.Empty to owner {OwnerId}",
                orphanMappings.Count, ownerId);
        }

        private class ComboSeries
        {
            public string MihonId { get; set; }
            public ParsedManga? Series { get; set; }
            public List<ParsedChapter> Chapters { get; set; } = [];
        }

    }

    /// <summary>Outcome of a single-chapter (re-)download request.</summary>
    public enum RedownloadOutcome
    {
        Queued,
        Paused,
        SeriesNotFound,
        NoSourceAvailable,
        ChapterNotFound
    }

    /// <summary>Result of <see cref="SeriesCommandService.RedownloadChapterAsync"/>.</summary>
    public class RedownloadResult
    {
        public RedownloadResult(RedownloadOutcome outcome, string? sourceProviderName = null, int queued = 0)
        {
            Outcome = outcome;
            SourceProviderName = sourceProviderName;
            Queued = queued;
        }

        public RedownloadOutcome Outcome { get; }
        public string? SourceProviderName { get; }
        public int Queued { get; }
    }
}

