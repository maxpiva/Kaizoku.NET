using System.Collections.Concurrent;
using RensaioBackend.Models.ReadState;
using RensaioBackend.Services.Scrobbling;

namespace RensaioBackend.Services.ReadState;

/// <summary>
/// Background service that consumes <see cref="ReadStateChangeEvent"/> from
/// <see cref="ReadStateChangeNotifier"/> and calls
/// <see cref="ScrobblerSyncService.UpdateForUserAndSeriesAsync"/> with a
/// 1-minute debounce per (userId, seriesId) pair.
///
/// This avoids circular DI: ReadStateService never references ScrobblerSyncService.
/// Instead it publishes events through the notifier, and this service resolves
/// ScrobblerSyncService from a child scope on demand.
/// </summary>
public sealed class DebouncedScrobblerSyncHostedService : BackgroundService
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly ReadStateChangeNotifier _notifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DebouncedScrobblerSyncHostedService> _logger;

    // Tracks the last event timestamp for each (userId, seriesId) pair.
    private readonly ConcurrentDictionary<(Guid UserId, Guid SeriesId), DateTime> _pending
        = new ConcurrentDictionary<(Guid UserId, Guid SeriesId), DateTime>();

    public DebouncedScrobblerSyncHostedService(
        ReadStateChangeNotifier notifier,
        IServiceScopeFactory scopeFactory,
        ILogger<DebouncedScrobblerSyncHostedService> logger)
    {
        _notifier = notifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the sweep timer that flushes expired entries.
        using var timer = new PeriodicTimer(SweepInterval);

        // Run both the channel consumer and the timer concurrently.
        var consumerTask = ConsumeChannelAsync(stoppingToken);
        var sweepTask = SweepLoopAsync(timer, stoppingToken);

        // Wait for both to complete — using WhenAll ensures neither is abandoned on cancellation.
        try
        {
            await Task.WhenAll(consumerTask, sweepTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task ConsumeChannelAsync(CancellationToken token)
    {
        var reader = _notifier.Reader;
        await foreach (var evt in reader.ReadAllAsync(token))
        {
            // Update or add the timestamp — this resets the debounce clock for this key.
            _pending[(evt.UserId, evt.SeriesId)] = DateTime.UtcNow;
        }
    }

    private async Task SweepLoopAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (_pending.IsEmpty)
                    continue;

                var now = DateTime.UtcNow;
                var ready = new List<(Guid, Guid)>();

                // Collect entries whose debounce window has elapsed.
                foreach (var kvp in _pending)
                {
                    if (now - kvp.Value >= DebounceWindow)
                    {
                        ready.Add(kvp.Key);
                    }
                }

                foreach (var key in ready)
                {
                    // If cancellation is requested, stop processing new sweeps
                    if (token.IsCancellationRequested)
                        break;

                    // Try to remove — only process if we successfully claimed it.
                    if (_pending.TryRemove(key, out _))
                    {
                        await ProcessEventAsync(key.Item1, key.Item2, token);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task ProcessEventAsync(Guid userId, Guid seriesId, CancellationToken token)
    {
        string name = seriesId.ToString();
        string username = userId.ToString();
        try
        {
            // Guard against shutdown — don't try to create a scope if the container is being disposed
            if (token.IsCancellationRequested)
                return;

            using var scope = _scopeFactory.CreateScope();
            var readStateService = scope.ServiceProvider.GetRequiredService<ReadStateService>();
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

            // Fetch the series to get the storage path
            var series = await db.Series.FindAsync(new object[] { seriesId }, token);
            name = series?.Title ?? name;
            if (series == null || string.IsNullOrWhiteSpace(series.StoragePath))
            {
                _logger.LogWarning("Series {name} not found or missing storage path, skipping scrobbler sync", name);
                return;
            }

            // Get the user to resolve username
            var user = await db.Users.FindAsync(new object[] { userId }, token);
            username = user?.Username ?? username;
            if (user == null)
            {
                _logger.LogWarning("User {username} not found, skipping scrobbler sync", username);
                return;
            }

            // Fetch current local read states for this user+series
            var localStates = readStateService.GetSeriesReadStates(user.Username, series.StoragePath);

            var syncService = scope.ServiceProvider.GetRequiredService<ScrobblerSyncService>();
            await syncService.UpdateForUserAndSeriesAsync(userId, seriesId, localStates, token);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown — swallow silently
        }
        catch (ObjectDisposedException)
        {
            // DI container disposed during shutdown — swallow silently
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to sync scrobbler read state for user {username} series {name}",
                username, name);
        }
    }
}
