using KaizokuBackend.Data;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Settings;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Daily
{
    public class DailyService
    {
        private readonly AppDbContext _db;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public DailyService(AppDbContext db, ILogger<DailyService> logger, IConfiguration configuration)
        {
            _db = db;
            _logger = logger;
            _configuration = configuration;
  
        }

        public async Task<JobResult> ExecuteAsync(JobInfo _, CancellationToken token = default)
        {
            await CreateBackupAsync(token).ConfigureAwait(false);
            await CleanupOldCompletedEnqueueAsync(token).ConfigureAwait(false);
            return JobResult.Success;
        }

        public async Task CreateBackupAsync(CancellationToken token = default)
        {
            string backupDirectory = Path.Combine(_configuration["runtimeDirectory"]!, "Backups");
            if (!Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.CreateDirectory(backupDirectory);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to create backup directory at {BackupDirectory}", backupDirectory);
                    return;
                }
            }
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(backupDirectory, $"backup-{timestamp}.db");
            string sqlCommand = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            await _db.Database.ExecuteSqlRawAsync(sqlCommand, token).ConfigureAwait(false);
            _logger.LogInformation("SQLite backup created at {backupPath}", backupPath);

            // Cleanup: keep only the 31 most recent backups
            try
            {
                var backupFiles = Directory.GetFiles(backupDirectory, "backup-*.db")
                    .OrderByDescending(f => f)
                    .ToList();

                if (backupFiles.Count > 31)
                {
                    var toDelete = backupFiles.Skip(31);
                    foreach (var file in toDelete)
                    {
                        try
                        {
                            File.Delete(file);
                            _logger.LogInformation("Deleted old backup file: {BackupFile}", file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old backup file: {BackupFile}", file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup old backup files in {BackupDirectory}", backupDirectory);
            }
        }
        public async Task<int> CleanupOldCompletedEnqueueAsync(CancellationToken token = default)
        {
            // Step 1: Get all completed items ordered by FinishedDate descending
            var completedItems = await _db.Queues
                .Where(q => q.Status == QueueStatus.Completed && q.FinishedDate != null)
                .OrderByDescending(q => q.FinishedDate)
                .ToListAsync(token)
                .ConfigureAwait(false);

            // Step 2: If there are <= 1000, do nothing
            if (completedItems.Count <= 1000)
                return 0;

            // Step 3: Find the cutoff date (1 month ago)
            var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

            // Step 4: Find the 1000th most recent completed item
            var minKeepDate = completedItems.Count > 1000
                ? completedItems[999].FinishedDate!.Value
                : oneMonthAgo;

            // Step 5: The cutoff is the later of oneMonthAgo or the 1000th item's FinishedDate
            var cutoffDate = minKeepDate > oneMonthAgo ? minKeepDate : oneMonthAgo;

            // Step 6: Find items to delete (older than cutoff)
            var toDelete = completedItems
                .Where(q => q.FinishedDate < cutoffDate)
                .ToList();

            if (toDelete.Count == 0)
                return 0;

            _db.Queues.RemoveRange(toDelete);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            _logger.LogInformation("Deleted {Count} old completed Enqueue items (cutoff: {Cutoff})", toDelete.Count, cutoffDate);
            return toDelete.Count;
        }
    }
}
