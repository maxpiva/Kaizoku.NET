using RensaioBackend.Data;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Scrobbling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RensaioBackend.Services.Jobs.Commands;

/// <summary>
/// Scheduled job that syncs read states for all users with enabled scrobblers.
/// Command discovery: class name "ScrobblerSync" matches JobType.ScrobblerSync.ToString().
/// </summary>
public class ScrobblerSync : ICommand
{
    public JobType JobType => JobType.ScrobblerSync;
    public Type? ParameterType => null;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScrobblerSync> _logger;

    public ScrobblerSync(IServiceScopeFactory scopeFactory, ILogger<ScrobblerSync> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<ScrobblerSyncService>();

        try
        {
            // Get all users who have at least one enabled scrobbler
            var userIds = await db.UserScrobblerConfigs
                .Where(c => c.IsEnabled)
                .Select(c => c.UserId)
                .Distinct()
                .ToListAsync(token);

            _logger.LogInformation("Scrobbler sync starting for {Count} users", userIds.Count);

            int successCount = 0;
            foreach (var userId in userIds)
            {
                try
                {
                    await syncService.SyncForUserAsync(userId, token);
                    successCount++;
                }
                catch (Exception ex)
                {
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(a=>a.Id==userId);
                    string username = user?.Username ?? userId.ToString();
                    _logger.LogError(ex, "Scrobbler sync failed for user {username}", username);
                }
            }

            _logger.LogInformation("Scrobbler sync completed for {Success}/{Total} users", successCount, userIds.Count);
            return JobResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scrobbler sync job failed");
            return JobResult.Failed;
        }
    }
}