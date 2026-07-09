using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using RensaioBackend.Services.ReadState;

namespace RensaioBackend.Services.Series;

/// <summary>
/// Central service for synchronizing series state between the database and rensaio.json files.
/// Ensures rensaio.json always reflects the current DB state while preserving UserReadStates.
/// Called after any metadata or file-state change (add, update, chapter fetch, download,
/// archive rename, cleanup, integrity verify, provider match, etc.).
///
/// All writes to rensaio.json are delegated to RensaioJsonService which provides
/// atomic read-modify-write with per-file locking, preventing lost-update races
/// with concurrent writes from ReadStateService.
/// </summary>
public class SeriesStateService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly RensaioJsonService _rensaioJsonService;
    private readonly ILogger<SeriesStateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SeriesStateService(
        AppDbContext db,
        SettingsService settings,
        RensaioJsonService rensaioJsonService,
        ILogger<SeriesStateService> logger)
    {
        _db = db;
        _settings = settings;
        _rensaioJsonService = rensaioJsonService;
        _logger = logger;
    }

    /// <summary>
    /// Syncs the current DB state to rensaio.json for a series.
    /// Always preserves UserReadStates from the existing rensaio.json.
    /// Called after any metadata or file-state change.
    /// </summary>
    public async Task SyncToRensaioJsonAsync(Guid seriesId, CancellationToken token = default)
    {
        SeriesEntity? series = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId, token)
            .ConfigureAwait(false);

        if (series == null)
        {
            _logger.LogWarning("Cannot sync rensaio.json: Series {SeriesId} not found", seriesId);
            return;
        }

        await SyncToRensaioJsonAsync(series, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Syncs the current DB state to rensaio.json using a pre-loaded SeriesEntity.
    /// This overload avoids double-loading the entity from the database.
    /// Always preserves UserReadStates from the existing rensaio.json.
    /// </summary>
    public async Task SyncToRensaioJsonAsync(SeriesEntity series, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        try
        {
            string? seriesFolder = _settings.DirectSettings?.ResolveSeriesAbsolutePath(series.StoragePath);
            if (string.IsNullOrEmpty(seriesFolder))
            {
                _logger.LogWarning("Cannot resolve series folder for Series {Title} with storage path {StoragePath}", series.Title, series.StoragePath);
                return;
            }

            // Step 1: Build snapshot from current DB state
            ImportSeriesSnapshot snapshot = series.ToImportSeriesSnapshot();

            // Step 1.5: Populate ExternalMappings from SeriesMappings table
            var mappings = await _db.SeriesMappings
                .Where(m => m.SeriesId == series.Id)
                .ToListAsync(token);

            if (mappings.Count > 0)
            {
                snapshot.Series.ExternalMappings = mappings.Select(a =>
                new ExternalMapping
                {
                    ExternalId = a.ExternalSeriesId,
                    Provider = a.Provider.ToString(),
                    ExternalTitle = a.ExternalSeriesTitle ?? ""
                }).ToList();
            }

            // Step 2 & 3: Atomically read existing rensaio.json, merge UserReadStates, and write
            // The per-file lock in RensaioJsonService ensures that concurrent writes from
            // ReadStateService do not interleave, preventing lost updates.
            await _rensaioJsonService.ModifyAsync(seriesFolder, existingSnapshot =>
            {
                if (existingSnapshot?.UserReadStates != null && existingSnapshot.UserReadStates.Count > 0)
                {
                    snapshot.UserReadStates = existingSnapshot.UserReadStates;
                    snapshot.Version = Math.Max(snapshot.Version, existingSnapshot.Version);
                }
                return snapshot;
            }, token).ConfigureAwait(false);

            _logger.LogDebug("Synced rensaio.json for series {SeriesId} ({Title})", series.Id, series.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing rensaio.json for series {Title}: {Message}", series.Title, ex.Message);
        }
    }

    /// <summary>
    /// Reads current state from rensaio.json for a series folder.
    /// Returns null if the file doesn't exist or is unreadable.
    /// </summary>
    public async Task<ImportSeriesSnapshot?> LoadFromRensaioJsonAsync(string seriesFolder, CancellationToken token = default)
    {
        return await _rensaioJsonService.LoadAsync(seriesFolder, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current DB state as an ImportSeriesSnapshot (without reading rensaio.json).
    /// Useful for verification, export, or when read state preservation isn't needed.
    /// </summary>
    public async Task<ImportSeriesSnapshot> GetCurrentSnapshotAsync(Guid seriesId, CancellationToken token = default)
    {
        SeriesEntity? series = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId, token)
            .ConfigureAwait(false);

        return series?.ToImportSeriesSnapshot() ?? new ImportSeriesSnapshot();
    }
}
