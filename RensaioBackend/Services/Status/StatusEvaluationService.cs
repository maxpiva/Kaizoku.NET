using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace RensaioBackend.Services.Status;

/// <summary>
/// Service responsible for evaluating health status of series and providers.
/// Runs periodically to detect stale series, failing providers, and other issues.
/// </summary>
public class StatusEvaluationService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly ILogger<StatusEvaluationService> _logger;

    public StatusEvaluationService(AppDbContext db, SettingsService settings, ILogger<StatusEvaluationService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates all series and providers for health status.
    /// Creates/updates HealthStatusEntity records as needed.
    /// </summary>
    public async Task EvaluateAllAsync(CancellationToken token = default)
    {
        SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        
        _logger.LogInformation("Starting health status evaluation...");
        
        await EvaluateSeriesAsync(settings, token).ConfigureAwait(false);
        await EvaluateProvidersAsync(settings, token).ConfigureAwait(false);
        
        _logger.LogInformation("Health status evaluation completed.");
    }

    /// <summary>
    /// Evaluates all series for release cadence violations.
    /// </summary>
    private async Task EvaluateSeriesAsync(SettingsDto settings, CancellationToken token = default)
    {
        var seriesList = await _db.Series
            .Include(s => s.Sources)
            .AsNoTracking()
            .ToListAsync(token)
            .ConfigureAwait(false);

        foreach (var series in seriesList)
        {
            await EvaluateSingleSeriesAsync(series, settings, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evaluates a single series for health status.
    /// Uses stored ReleaseCadenceDays if available, falls back to default settings.
    /// Checks provider status before creating alerts to avoid false warnings
    /// when a series has gone on hiatus or completed.
    /// </summary>
    public async Task EvaluateSingleSeriesAsync(SeriesEntity series, SettingsDto settings, CancellationToken token = default)
    {
        // Skip series that are completed, cancelled, hiatus, or finished — they don't need monitoring
        if (series.Status == SeriesStatus.COMPLETED ||
            series.Status == SeriesStatus.CANCELLED ||
            series.Status == SeriesStatus.ON_HIATUS ||
            series.Status == SeriesStatus.PUBLISHING_FINISHED)
        {
            await ClearActiveAlertAsync(HealthStatusTargetType.Series, series.Id, token).ConfigureAwait(false);
            return;
        }

        // Skip series with no last chapter date (never had chapters)
        if (series.LastChapterDate == null)
        {
            await ClearActiveAlertAsync(HealthStatusTargetType.Series, series.Id, token).ConfigureAwait(false);
            return;
        }

        // Check provider-level status before creating alerts.
        // COMPLETED, ON_HIATUS, PUBLISHING_FINISHED: if ANY single provider reports this,
        // the series-level status may be stale — clear alerts and return.
        // CANCELLED: only suppress if ALL active providers (and there's more than one) report cancelled.
        var activeProviders = series.Sources
            .Where(s => !s.IsDisabled && !s.IsUninstalled && !s.IsUnknown)
            .ToList();

        bool anyDefinitiveEnd = activeProviders.Any(p =>
            p.LastKnownStatus == SeriesStatus.COMPLETED ||
            p.LastKnownStatus == SeriesStatus.ON_HIATUS ||
            p.LastKnownStatus == SeriesStatus.PUBLISHING_FINISHED);

        if (anyDefinitiveEnd)
        {
            _logger.LogInformation(
                "Series {Title} has a provider reporting definitive end status, clearing alerts",
                series.Title);
            await ClearActiveAlertAsync(HealthStatusTargetType.Series, series.Id, token).ConfigureAwait(false);
            return;
        }

        // CANCELLED: only suppress if ALL active providers report cancelled
        // (and there are multiple providers — a single provider being cancelled might just mean
        // the user removed that source, not that the series is truly cancelled)
        if (activeProviders.Count > 0 && activeProviders.All(p => p.LastKnownStatus == SeriesStatus.CANCELLED))
        {
            _logger.LogInformation(
                "Series {Title} all {Count} active providers report CANCELLED, clearing alerts",
                series.Title, activeProviders.Count);
            await ClearActiveAlertAsync(HealthStatusTargetType.Series, series.Id, token).ConfigureAwait(false);
            return;
        }

        // Use stored cadence if available, otherwise fall back to default.
        // ReleaseCadenceDays may be negative (user-set) — use Math.Abs to get the actual value.
        double estimatedCadenceDays;
        if (series.ReleaseCadenceDays.HasValue && series.ReleaseCadenceDays.Value != 0)
        {
            estimatedCadenceDays = Math.Abs((double)series.ReleaseCadenceDays.Value);
        }
        else
        {
            estimatedCadenceDays = settings.ReleaseCadenceDefaultDays;
        }

        double daysSinceLastChapter = (DateTime.UtcNow - series.LastChapterDate.Value).TotalDays;

        // Diagnostic logging for debugging threshold issues
        _logger.LogDebug(
            "Evaluating series {Title}: cadence={Cadence:F1}d, daysSince={Days:F1}d, multYellow={Yellow}, multRed={Red}",
            series.Title, estimatedCadenceDays, daysSinceLastChapter,
            settings.ReleaseCadenceMultiplierYellow, settings.ReleaseCadenceMultiplierRed);

        // Safety guard: skip alert creation if multipliers are zero or negative.
        // This prevents false positives when settings are corrupted or culture-misparsed.
        if (settings.ReleaseCadenceMultiplierYellow <= 0d || settings.ReleaseCadenceMultiplierRed <= 0d)
        {
            _logger.LogWarning(
                "Series {Title} has invalid release cadence multipliers (Yellow: {Y}, Red: {R}), skipping alert evaluation",
                series.Title, settings.ReleaseCadenceMultiplierYellow, settings.ReleaseCadenceMultiplierRed);
            return;
        }

        // Check if the alert was recently dismissed (suppression period = one cadence cycle).
        // When the user dismisses an alert, we honor that decision and won't recreate it
        // until at least one full cadence period has elapsed since the dismissal.
        var lastResolvedAlert = await _db.HealthStatuses
            .Where(h => h.TargetType == HealthStatusTargetType.Series
                        && h.TargetId == series.Id
                        && !h.IsActive
                        && h.ResolvedAt != null)
            .OrderByDescending(h => h.ResolvedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (lastResolvedAlert?.ResolvedAt != null)
        {
            DateTime suppressionDeadline = lastResolvedAlert.ResolvedAt.Value.AddDays(estimatedCadenceDays);
            if (DateTime.UtcNow < suppressionDeadline)
            {
                _logger.LogDebug(
                    "Series {Title} alert was dismissed at {ResolvedAt}, suppression active until {Deadline} ({Cadence:F1} days)",
                    series.Title, lastResolvedAlert.ResolvedAt, suppressionDeadline, estimatedCadenceDays);
                return;
            }
        }

        // Check for RED: no active sources AND past red threshold
        if (activeProviders.Count == 0 && daysSinceLastChapter >= estimatedCadenceDays * settings.ReleaseCadenceMultiplierRed)
        {
            await UpsertAlertAsync(HealthStatusTargetType.Series, series.Id,
                HealthStatusLevel.Red,
                $"No active sources for {(int)daysSinceLastChapter} days (all providers down or unassigned)",
                token).ConfigureAwait(false);
            return;
        }

        // Check for YELLOW: past yellow threshold
        if (daysSinceLastChapter >= estimatedCadenceDays * settings.ReleaseCadenceMultiplierYellow)
        {
            await UpsertAlertAsync(HealthStatusTargetType.Series, series.Id,
                HealthStatusLevel.Yellow,
                $"No new chapters for {(int)daysSinceLastChapter} days (expected cadence: {estimatedCadenceDays:F1} days)",
                token).ConfigureAwait(false);
            return;
        }

        // All good — clear any existing alert
        await ClearActiveAlertAsync(HealthStatusTargetType.Series, series.Id, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates all providers for error-based health status.
    /// </summary>
    private async Task EvaluateProvidersAsync(SettingsDto settings, CancellationToken token = default)
    {
        var providers = await _db.SeriesProviders
            .Where(p => !p.IsDisabled && !p.IsUninstalled && !p.IsUnknown)
            .AsNoTracking()
            .ToListAsync(token)
            .ConfigureAwait(false);

        foreach (var provider in providers)
        {
            await EvaluateSingleProviderAsync(provider, settings, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evaluates a single provider for health status based on error history.
    /// </summary>
    public async Task EvaluateSingleProviderAsync(SeriesProviderEntity provider, SettingsDto settings, CancellationToken token = default)
    {
        // No errors recorded — clear any alert
        if (provider.LastErrorDate == null || provider.ConsecutiveErrorCount == 0)
        {
            await ClearActiveAlertAsync(HealthStatusTargetType.Provider, provider.Id, token).ConfigureAwait(false);
            return;
        }

        double hoursSinceLastError = (DateTime.UtcNow - provider.LastErrorDate.Value).TotalHours;

        // Check for RED: errors persisting beyond red threshold
        if (hoursSinceLastError >= settings.ProviderErrorRedHours)
        {
            // Find affected series for this provider
            var affectedSeries = await _db.SeriesProviders
                .Where(sp => sp.Id == provider.Id)
                .Join(_db.Series, sp => sp.SeriesId, s => s.Id, (sp, s) => s.Title)
                .ToListAsync(token)
                .ConfigureAwait(false);

            string affectedJson = System.Text.Json.JsonSerializer.Serialize(affectedSeries);

            await UpsertAlertAsync(HealthStatusTargetType.Provider, provider.Id,
                HealthStatusLevel.Red,
                $"Provider has been failing for {(int)hoursSinceLastError} hours ({provider.ConsecutiveErrorCount} consecutive errors)",
                token, affectedJson).ConfigureAwait(false);
            return;
        }

        // Check for YELLOW: errors persisting beyond yellow threshold
        if (hoursSinceLastError >= settings.ProviderErrorYellowHours)
        {
            var affectedSeries = await _db.SeriesProviders
                .Where(sp => sp.Id == provider.Id)
                .Join(_db.Series, sp => sp.SeriesId, s => s.Id, (sp, s) => s.Title)
                .ToListAsync(token)
                .ConfigureAwait(false);

            string affectedJson = System.Text.Json.JsonSerializer.Serialize(affectedSeries);

            await UpsertAlertAsync(HealthStatusTargetType.Provider, provider.Id,
                HealthStatusLevel.Yellow,
                $"Provider has been failing for {(int)hoursSinceLastError} hours ({provider.ConsecutiveErrorCount} consecutive errors)",
                token, affectedJson).ConfigureAwait(false);
            return;
        }

        // Errors exist but haven't crossed the yellow threshold yet — no alert
        await ClearActiveAlertAsync(HealthStatusTargetType.Provider, provider.Id, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates or updates an active health alert for the given target.
    /// If an active alert already exists for the same target, it's updated in-place
    /// rather than creating a new record. This ensures only one active alert
    /// exists per target at any time.
    /// </summary>
    private async Task UpsertAlertAsync(HealthStatusTargetType targetType, Guid targetId,
        HealthStatusLevel level, string message, CancellationToken token = default,
        string? affectedSeriesJson = null)
    {
        var existing = await _db.HealthStatuses
            .FirstOrDefaultAsync(h => h.TargetType == targetType && h.TargetId == targetId && h.IsActive, token)
            .ConfigureAwait(false);

        if (existing != null)
        {
            if (existing.Level == level && existing.Message == message)
                return; // No change needed

            // Update the existing alert in-place instead of creating a new one
            existing.Level = level;
            existing.Message = message;
            existing.AffectedSeriesJson = affectedSeriesJson;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            var alert = new HealthStatusEntity
            {
                Id = Guid.NewGuid(),
                TargetType = targetType,
                TargetId = targetId,
                Level = level,
                Message = message,
                AffectedSeriesJson = affectedSeriesJson,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.HealthStatuses.Add(alert);
        }

        await _db.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears any active alert for the given target (marks as resolved).
    /// </summary>
    private async Task ClearActiveAlertAsync(HealthStatusTargetType targetType, Guid targetId, CancellationToken token = default)
    {
        var existing = await _db.HealthStatuses
            .FirstOrDefaultAsync(h => h.TargetType == targetType && h.TargetId == targetId && h.IsActive, token)
            .ConfigureAwait(false);

        if (existing == null)
            return;

        existing.IsActive = false;
        existing.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(token).ConfigureAwait(false);
    }
}
