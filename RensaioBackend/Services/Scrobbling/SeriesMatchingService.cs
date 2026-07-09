using RensaioBackend.Data;
using RensaioBackend.Models;
using RensaioBackend.Services.Images;
using RensaioBackend.Services.Settings;
using RensaioBackend.Extensions;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Scrobbling.Abstractions;
using RensaioBackend.Services.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RensaioBackend.Services.Scrobbling;

/// <summary>
/// Handles automatic and manual series matching between local series and external scrobbling services.
/// Provides auto-matching with 95% word-coverage threshold, manual search/confirm, and disable logic.
/// Token lifecycle is delegated to each provider's EnsureAuthenticatedAsync.
/// 
/// Cascading match order:
/// 1. SeriesMappings (global table, shared across users, role-based overwrite)
/// 2. ImportSeriesSnapshot.ExternalMappings (from rensaio.json / DB snapshot)
/// 3. Scrobbler provider search (original behavior)
/// </summary>
public class SeriesMatchingService
{
    private readonly AppDbContext _db;
    private readonly ScrobblerProviderFactory _providerFactory;
    private readonly TitleMatcher _titleMatcher;
    private readonly SeriesStateService _seriesStateService;
    private readonly SettingsService _settings;
    private readonly ThumbCacheService _thumbCache;
    private readonly ILogger<SeriesMatchingService> _logger;
    private const string ImagePrefix = "/api/image/";

    public SeriesMatchingService(
        AppDbContext db,
        ScrobblerProviderFactory providerFactory,
        TitleMatcher titleMatcher,
        SeriesStateService seriesStateService,
        SettingsService settings,
        ThumbCacheService thumbCache,
        ILogger<SeriesMatchingService> logger)
    {
        _db = db;
        _providerFactory = providerFactory;
        _titleMatcher = titleMatcher;
        _seriesStateService = seriesStateService;
        _settings = settings;
        _thumbCache = thumbCache;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a series thumbnail URL to an /api/image/{key} endpoint URL.
    /// </summary>
    private async ValueTask<string?> ResolveCoverUrlAsync(string? thumbnailUrl, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(thumbnailUrl))
            return null;
        var key = await _thumbCache.GetKeyAsync(thumbnailUrl, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(key))
            return null;
        return ImagePrefix + key;
    }

    /// <summary>
    /// Try to auto-match a series by title across all enabled scrobblers.
    /// Uses cascading matching: exact normalized > alternate titles > word-coverage.
    /// </summary>
    public async Task AutoMatchSeriesAsync(Guid userId, Guid seriesId, CancellationToken token = default)
    {
        var configs = await _db.UserScrobblerConfigs
            .Where(c => c.UserId == userId && c.IsEnabled)
            .ToListAsync(token);

        var series = await _db.Series
            .Include(s => s.Sources)
            .FirstOrDefaultAsync(s => s.Id == seriesId, token);

        if (series == null) return;

        foreach (var config in configs)
        {
            await AutoMatchForProviderAsync(userId, series, config.Provider, token);
        }
    }

    /// <summary>
    /// Auto-match all unmatched series for a user across a specific provider.
    /// </summary>
    public async Task<AutoMatchResultDto> AutoMatchAllAsync(Guid userId, ScrobblerProvider provider, CancellationToken token = default)
    {
        var result = new AutoMatchResultDto();
        var config = await _db.UserScrobblerConfigs
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, token);

        if (config == null || !config.IsEnabled)
            return result;

        var seriesList = await _db.Series
            .Include(s => s.Sources)
            .ToListAsync(token);

        result.TotalSeries = seriesList.Count;

        foreach (var series in seriesList)
        {
            // Check user-level mapping first
            var existingMapping = await _db.UserSeriesMappings
                .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == series.Id
                    && m.Provider == provider, token);

            if (existingMapping != null && existingMapping.MappingStatus != SeriesMappingStatus.Unmatched)
            {
                result.LeftUnmatched++;
                continue;
            }

            // Check if a global SeriesMapping exists (cascade source 1)
            var globalMapping = await _db.SeriesMappings
                .FirstOrDefaultAsync(m => m.SeriesId == series.Id && m.Provider == provider, token);

            if (globalMapping != null && !string.IsNullOrEmpty(globalMapping.ExternalSeriesId))
            {
                // Auto-create user mapping from global mapping
                if (existingMapping == null)
                {
                    _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        SeriesId = series.Id,
                        Provider = provider,
                        ExternalSeriesId = globalMapping.ExternalSeriesId,
                        ExternalSeriesTitle = globalMapping.ExternalSeriesTitle,
                        MappingStatus = SeriesMappingStatus.AutoMatched
                    });
                    await _db.SaveChangesAsync(token);
                }
                else
                {
                    existingMapping.ExternalSeriesId = globalMapping.ExternalSeriesId;
                    existingMapping.ExternalSeriesTitle = globalMapping.ExternalSeriesTitle;
                    existingMapping.MappingStatus = SeriesMappingStatus.AutoMatched;
                    await _db.SaveChangesAsync(token);
                }

                result.AutoMatched++;
                continue;
            }

            // Fall through to scrobbler provider search (cascade source 3)
            var matchResult = await TryAutoMatchAsync(userId, series, provider, token);
            if (matchResult != null)
            {
                result.AutoMatched++;
                if (matchResult.MatchScore.HasValue && matchResult.MatchScore.Value < 0.95)
                {
                    result.SuggestedMatches.Add(matchResult);
                }
            }
            else
            {
                result.LeftUnmatched++;
            }
        }

        return result;
    }

    /// <summary>
    /// Search for a series on an external scrobbling service.
    /// Loads the user's stored token via EnsureAuthenticatedAsync, then calls the provider search.
    /// </summary>
    public async Task<List<ScrobblerSearchResult>> SearchExternalSeriesAsync(
        Guid userId, ScrobblerProvider provider, string query, CancellationToken token = default)
    {
        var scrobbler = _providerFactory.GetProvider(provider);
        if (scrobbler == null) return [];

        await scrobbler.EnsureAuthenticatedAsync(userId, token);

        return await scrobbler.SearchSeriesAsync(query, token);
    }

    /// <summary>
    /// Confirm a manual match between a local series and an external series ID.
    /// Creates a mapping with UserConfirmed status.
    /// Also upserts the global SeriesMapping and syncs rensaio.json.
    /// </summary>
    public async Task ConfirmMatchAsync(Guid userId, Guid seriesId, ScrobblerProvider provider,
        string externalSeriesId, string? externalTitle = null, CancellationToken token = default)
    {
        var mapping = await _db.UserSeriesMappings
            .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == seriesId
                && m.Provider == provider, token);

        if (mapping != null)
        {
            mapping.ExternalSeriesId = externalSeriesId;
            mapping.ExternalSeriesTitle = externalTitle;
            mapping.MappingStatus = SeriesMappingStatus.UserConfirmed;
        }
        else
        {
            _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SeriesId = seriesId,
                Provider = provider,
                ExternalSeriesId = externalSeriesId,
                ExternalSeriesTitle = externalTitle,
                MappingStatus = SeriesMappingStatus.UserConfirmed
            });
        }

        await _db.SaveChangesAsync(token);

        // Also upsert global SeriesMapping (no raw data on manual confirm)
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
        await UpsertSeriesMappingAsync(userId, seriesId, provider, externalSeriesId, externalTitle,
             user?.Level ?? UserLevel.User, token);
    }

    /// <summary>
    /// Get all series that are unmatched for a user across all scrobblers.
    /// </summary>
    public async Task<List<SeriesMatchStatusDto>> GetUnmatchedSeriesAsync(Guid userId, CancellationToken token = default)
    {
        var configs = await _db.UserScrobblerConfigs
            .Where(c => c.UserId == userId && c.IsEnabled)
            .ToListAsync(token);

        var result = new List<SeriesMatchStatusDto>();

        foreach (var config in configs)
        {
            var seriesList = await _db.Series
                .Include(s => s.Sources)
                .ToListAsync(token);

            foreach (var series in seriesList)
            {
                var mapping = await _db.UserSeriesMappings
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == series.Id
                        && m.Provider == config.Provider, token);

                var altTitles = string.Join(", ", series.Sources?
                    .Where(s => !string.IsNullOrEmpty(s.Title)
                             && !s.Title.Equals(series.Title, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Title)
                    .Distinct() ?? []);

                var coverUrl = await ResolveCoverUrlAsync(series.ThumbnailUrl, token);
                result.Add(new SeriesMatchStatusDto
                {
                    SeriesId = series.Id,
                    SeriesTitle = series.Title,
                    SeriesCoverUrl = coverUrl,
                    AlternativeTitles = altTitles,
                    Provider = config.Provider,
                    MappingStatus = mapping?.MappingStatus ?? SeriesMappingStatus.Unmatched,
                    ExternalSeriesId = mapping?.ExternalSeriesId,
                    ExternalSeriesTitle = mapping?.ExternalSeriesTitle,
                    MatchScore = null
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Mark a series as explicitly disabled/linked for a specific scrobbler.
    /// Creates a mapping with Ignored status so the sync service skips it.
    /// </summary>
    public async Task DisableSeriesLinkAsync(Guid userId, Guid seriesId, ScrobblerProvider provider, CancellationToken token = default)
    {
        var mapping = await _db.UserSeriesMappings
            .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == seriesId
                && m.Provider == provider, token);

        if (mapping != null)
        {
            mapping.MappingStatus = SeriesMappingStatus.Ignored;
        }
        else
        {
            _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SeriesId = seriesId,
                Provider = provider,
                ExternalSeriesId = string.Empty,
                MappingStatus = SeriesMappingStatus.Ignored
            });
        }

        await _db.SaveChangesAsync(token);
    }

    /// <summary>
    /// Remove a mapping (reset to Unmatched).
    /// </summary>
    public async Task RemoveMappingAsync(Guid userId, Guid seriesId, ScrobblerProvider provider, CancellationToken token = default)
    {
        var mapping = await _db.UserSeriesMappings
            .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == seriesId
                && m.Provider == provider, token);

        if (mapping != null)
        {
            _db.UserSeriesMappings.Remove(mapping);
            await _db.SaveChangesAsync(token);
        }
    }

    /// <summary>
    /// Get all match statuses for a user.
    /// </summary>
    public async Task<List<SeriesMatchStatusDto>> GetMatchStatusesAsync(Guid userId, CancellationToken token = default)
    {
        var allSeries = await _db.Series
            .Include(s => s.Sources)
            .ToListAsync(token);
        var allMappings = await _db.UserSeriesMappings
            .Where(m => m.UserId == userId)
            .ToListAsync(token);

        var result = new List<SeriesMatchStatusDto>();
        var allProviders = Enum.GetValues<ScrobblerProvider>();

        foreach (var series in allSeries)
        {
            var altTitles = string.Join(", ", series.Sources?
                .Where(s => !string.IsNullOrEmpty(s.Title)
                         && !s.Title.Equals(series.Title, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Title)
                .Distinct() ?? []);

            foreach (var provider in allProviders)
            {
                var mapping = allMappings.FirstOrDefault(m =>
                    m.SeriesId == series.Id && m.Provider == provider);

                var externalId = mapping?.ExternalSeriesId;
                string? externalUrl = null;
                if (!string.IsNullOrEmpty(externalId))
                {
                    var prov = _providerFactory.GetProvider(provider);
                    if (prov?.SeriesUrlTemplate != null)
                    {
                        try { externalUrl = string.Format(prov.SeriesUrlTemplate, externalId); }
                        catch { /* ignore format errors */ }
                    }
                }

                var coverUrl = await ResolveCoverUrlAsync(series.ThumbnailUrl, token);
                result.Add(new SeriesMatchStatusDto
                {
                    SeriesId = series.Id,
                    SeriesTitle = series.Title,
                    SeriesCoverUrl = coverUrl,
                    AlternativeTitles = altTitles,
                    Provider = provider,
                    MappingStatus = mapping?.MappingStatus ?? SeriesMappingStatus.Unmatched,
                    ExternalSeriesId = externalId,
                    ExternalSeriesTitle = mapping?.ExternalSeriesTitle,
                    ExternalSeriesUrl = externalUrl,
                    MatchScore = null
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Get sync status per provider for a user.
    /// </summary>
    public async Task<List<SyncStatusDto>> GetSyncStatusAsync(Guid userId, CancellationToken token = default)
    {
        var configs = await _db.UserScrobblerConfigs
            .Where(c => c.UserId == userId)
            .ToListAsync(token);

        var result = new List<SyncStatusDto>();

        foreach (var config in configs)
        {
            var seriesMatched = await _db.UserSeriesMappings
                .CountAsync(m => m.UserId == userId && m.Provider == config.Provider
                    && (m.MappingStatus == SeriesMappingStatus.UserConfirmed
                        || m.MappingStatus == SeriesMappingStatus.AutoMatched), token);

            var seriesUnmatched = await _db.UserSeriesMappings
                .CountAsync(m => m.UserId == userId && m.Provider == config.Provider
                    && m.MappingStatus == SeriesMappingStatus.Unmatched, token);

            var seriesIgnored = await _db.UserSeriesMappings
                .CountAsync(m => m.UserId == userId && m.Provider == config.Provider
                    && m.MappingStatus == SeriesMappingStatus.Ignored, token);

            result.Add(new SyncStatusDto
            {
                Provider = config.Provider,
                LastSyncAt = config.LastSyncAt,
                LastUploadAt = config.LastUploadAt,
                LastDownloadAt = config.LastDownloadAt,
                SeriesMatched = seriesMatched,
                SeriesUnmatched = seriesUnmatched,
                SeriesIgnored = seriesIgnored
            });
        }

        return result;
    }

    // ── Private ──

    private async Task AutoMatchForProviderAsync(Guid userId, SeriesEntity series,
        ScrobblerProvider provider, CancellationToken token)
    {
        await TryAutoMatchAsync(userId, series, provider, token);
    }

    /// <summary>
    /// Cascading lookup for an existing mapping:
    /// 1. SeriesMappings (global table, shared across users)
    /// 2. ImportSeriesSnapshot.ExternalMappings (from rensaio.json / DB snapshot)
    /// Returns the match info if found via cascade sources 1 or 2.
    /// </summary>
    private async Task<(string? ExternalId, string? ExternalTitle)?> TryGetExistingMappingFromCascadeAsync(
        SeriesEntity? series, ScrobblerProvider provider, CancellationToken token)
    {
        // Step 1: Check global SeriesMappings table
        if (series != null)
        {
            var globalMapping = await _db.SeriesMappings
                .FirstOrDefaultAsync(m => m.SeriesId == series.Id && m.Provider == provider, token);

            if (globalMapping != null && !string.IsNullOrEmpty(globalMapping.ExternalSeriesId))
            {
                _logger.LogDebug("Found SeriesMapping cascade for series {Title} provider {Provider}: {ExternalId}",
                    series.Title, provider, globalMapping.ExternalSeriesId);
                return (globalMapping.ExternalSeriesId, globalMapping.ExternalSeriesTitle);
            }
        }

        // Step 2: Check rensaio.json on disk for persisted ExternalMappings
        if (series != null && !string.IsNullOrEmpty(series.StoragePath))
        {
            try
            {
                var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
                var seriesFolder = System.IO.Path.Combine(settings.StorageFolder, series.StoragePath);
                var rensaioJsonPath = System.IO.Path.Combine(seriesFolder, "rensaio.json");

                if (File.Exists(rensaioJsonPath))
                {
                    var jsonContent = await File.ReadAllTextAsync(rensaioJsonPath, token).ConfigureAwait(false);
                    var snapshot = System.Text.Json.JsonSerializer.Deserialize<ImportSeriesSnapshot>(jsonContent);

                    if (snapshot?.Series.ExternalMappings!=null)
                    {
                        ExternalMapping? mapping = snapshot.Series.ExternalMappings.FirstOrDefault(m => m.Provider == provider.ToString());
                        if (mapping!=null)
                        {
                            _logger.LogDebug("Found rensaio.json ExternalMappings cascade for series {Title} ({SeriesId}) provider {Provider}: {ExternalId} {ExternalTitle}",series.Title, series.Id, mapping.Provider, mapping.ExternalId, mapping.ExternalTitle);
                            return (mapping.ExternalId, mapping.ExternalTitle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read rensaio.json for ExternalMappings cascade (series {title})", series.Title);
            }
        }

        return null;
    }

    /// <summary>
    /// Upserts a global SeriesMapping with role-based overwrite protection.
    /// Only overwrites existing mapping if the new user's level >= existing user's level.
    /// After upserting, syncs rensaio.json via SeriesStateService.
    /// </summary>
    private async Task UpsertSeriesMappingAsync(Guid userId, Guid seriesId, ScrobblerProvider provider,
        string externalSeriesId, string? externalTitle, UserLevel userLevel,
        CancellationToken token)
    {
        var existing = await _db.SeriesMappings
            .FirstOrDefaultAsync(m => m.SeriesId == seriesId && m.Provider == provider, token);

        if (existing != null)
        {
            // Only overwrite if the new user's level is >= existing user's level
            if (userLevel >= existing.UserRole)
            {
                existing.ExternalSeriesId = externalSeriesId;
                existing.ExternalSeriesTitle = externalTitle;
                existing.UserUid = userId;
                existing.UserRole = userLevel;
                existing.UpdateDate = DateTime.UtcNow;
            }
            else
            {
                _logger.LogDebug(
                    "Skipping SeriesMapping overwrite for series {SeriesId} provider {Provider}: " +
                    "new user level {NewLevel} < existing user level {ExistingLevel}",
                    seriesId, provider, userLevel, existing.UserRole);
                // Don't overwrite, but still sync rensaio.json to ensure consistency
            }
        }
        else
        {
            _db.SeriesMappings.Add(new SeriesMappingEntity
            {
                Id = Guid.NewGuid(),
                SeriesId = seriesId,
                Provider = provider,
                ExternalSeriesId = externalSeriesId,
                ExternalSeriesTitle = externalTitle,
                UserUid = userId,
                UserRole = userLevel,
                UpdateDate = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(token);

        // After upserting, sync rensaio.json via SeriesStateService
        await _seriesStateService.SyncToRensaioJsonAsync(seriesId, token);
    }

    private async Task<SeriesMatchStatusDto?> TryAutoMatchAsync(Guid userId, SeriesEntity series,
        ScrobblerProvider provider, CancellationToken token)
    {
        // Step 0: Cascading match check — check global mappings before hitting the scrobbler
        var cascadeResult = await TryGetExistingMappingFromCascadeAsync(series, provider, token);
        if (cascadeResult.HasValue)
        {
            var (existingId, existingTitle) = cascadeResult.Value;

            // Create or update user-level mapping from cascade
            var existingUserMapping = await _db.UserSeriesMappings
                .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == series.Id
                    && m.Provider == provider, token);

            if (existingUserMapping != null)
            {
                existingUserMapping.ExternalSeriesId = existingId;
                existingUserMapping.ExternalSeriesTitle = existingTitle;
                existingUserMapping.MappingStatus = SeriesMappingStatus.AutoMatched;
            }
            else
            {
                _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SeriesId = series.Id,
                    Provider = provider,
                    ExternalSeriesId = existingId,
                    ExternalSeriesTitle = existingTitle,
                    MappingStatus = SeriesMappingStatus.AutoMatched
                });
            }

            await _db.SaveChangesAsync(token);

            return new SeriesMatchStatusDto
            {
                SeriesId = series.Id,
                SeriesTitle = series.Title,
                Provider = provider,
                MappingStatus = SeriesMappingStatus.AutoMatched,
                ExternalSeriesId = existingId,
                ExternalSeriesTitle = existingTitle,
                MatchScore = 1.0 // 100% — it's an existing mapping
            };
        }

        var scrobbler = _providerFactory.GetProvider(provider);
        if (scrobbler == null) return null;

        await scrobbler.EnsureAuthenticatedAsync(userId, token);

        // Step 1: build deduped local title candidates
        var localCandidates = _titleMatcher.BuildTitleCandidates(series);
        var uniqueTitles = localCandidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueTitles.Length == 0) return null;

        // Step 2: search each unique title against the scrobbler,
        //          collecting ALL results keyed by external ID (deduped).
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCandidates = new List<(string SearchTitle, string Id)>();
        var resultLookup = new Dictionary<string, ScrobblerSearchResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in uniqueTitles)
        {
            var searchResults = await scrobbler.SearchSeriesAsync(title, token);

            foreach (var result in searchResults)
            {
                if (string.IsNullOrWhiteSpace(result.ExternalId)) continue;
                if (string.IsNullOrWhiteSpace(result.Title)) continue;

                // Dedup by external ID across all searches (first title wins for lookup).
                if (seenIds.Add(result.ExternalId))
                {
                    allCandidates.Add((result.Title, result.ExternalId));
                    foreach(string alttitle in result.AlternateTitles)
                    {
                        allCandidates.Add((alttitle, result.ExternalId));
                    }
                    resultLookup[result.ExternalId] = result;
                }
            }
        }

        if (allCandidates.Count == 0) return null;

        // Step 3: score all unique results against all local candidates at once
        var scored = TitleMatcher.MatchTitles(
            originalTitles: localCandidates,
            candidates: allCandidates,
            minimumScore: 0);

        if (scored.Length == 0) return null;

        // Step 4: pick the best match
        var best = scored[0];
        var bestPercentage = best.Percentage;
        var bestExternalId = best.Id;
        var bestExternalTitle = best.SearchTitle;
        var bestResult = resultLookup.GetValueOrDefault(bestExternalId);

        const int autoMatchThreshold = 95;

        if (bestPercentage < autoMatchThreshold)
        {
            // Suggest as candidate (below threshold)
            // NOTE: This does NOT touch the global SeriesMappings table — only auto-approved
            // (>=95%) or user-confirmed matches create/update global mappings.
            var existingMapping = await _db.UserSeriesMappings
                .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == series.Id
                    && m.Provider == provider, token);

            if (existingMapping == null)
            {
                _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SeriesId = series.Id,
                    Provider = provider,
                    ExternalSeriesId = bestExternalId,
                    ExternalSeriesTitle = bestExternalTitle,
                    MappingStatus = SeriesMappingStatus.Unmatched
                });
                await _db.SaveChangesAsync(token);
            }

            var altTitlesSuggest = string.Join(", ", series.Sources?
                .Where(s => !string.IsNullOrEmpty(s.Title)
                         && !s.Title.Equals(series.Title, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Title)
                .Distinct() ?? []);

            var coverUrlSuggest = await ResolveCoverUrlAsync(series.ThumbnailUrl, token);
            return new SeriesMatchStatusDto
            {
                SeriesId = series.Id,
                SeriesTitle = series.Title,
                SeriesCoverUrl = coverUrlSuggest,
                AlternativeTitles = altTitlesSuggest,
                Provider = provider,
                MappingStatus = SeriesMappingStatus.Unmatched,
                ExternalSeriesId = bestExternalId,
                ExternalSeriesTitle = bestExternalTitle,
                ExternalCoverUrl = bestResult?.CoverUrl,
                MatchScore = bestPercentage / 100.0
            };
        }

        // Auto-match (>=95%) — only auto-approved matches create/update global SeriesMappings

        // Load user-level for role-based overwrite
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
        var userLevel = user?.Level ?? UserLevel.User;

        var existing = await _db.UserSeriesMappings
            .FirstOrDefaultAsync(m => m.UserId == userId && m.SeriesId == series.Id
                && m.Provider == provider, token);

        if (existing != null)
        {
            existing.ExternalSeriesId = bestExternalId;
            existing.ExternalSeriesTitle = bestExternalTitle;
            existing.MappingStatus = SeriesMappingStatus.AutoMatched;
        }
        else
        {
            _db.UserSeriesMappings.Add(new UserSeriesMappingEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SeriesId = series.Id,
                Provider = provider,
                ExternalSeriesId = bestExternalId,
                ExternalSeriesTitle = bestExternalTitle,
                MappingStatus = SeriesMappingStatus.AutoMatched
            });
        }

        await _db.SaveChangesAsync(token);

        // Upsert global SeriesMapping with externalRaw and role-check
        await UpsertSeriesMappingAsync(userId, series.Id, provider, bestExternalId,
            bestExternalTitle, userLevel, token);

        var altTitlesAuto = string.Join(", ", series.Sources?
            .Where(s => !string.IsNullOrEmpty(s.Title)
                     && !s.Title.Equals(series.Title, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Title)
            .Distinct() ?? []);

        var coverUrlAuto = await ResolveCoverUrlAsync(series.ThumbnailUrl, token);
        return new SeriesMatchStatusDto
        {
            SeriesId = series.Id,
            SeriesTitle = series.Title,
            SeriesCoverUrl = coverUrlAuto,
            AlternativeTitles = altTitlesAuto,
            Provider = provider,
            MappingStatus = SeriesMappingStatus.AutoMatched,
            ExternalSeriesId = bestExternalId,
            ExternalSeriesTitle = bestExternalTitle,
            ExternalCoverUrl = bestResult?.CoverUrl,
            MatchScore = bestPercentage / 100.0
        };
    }
}