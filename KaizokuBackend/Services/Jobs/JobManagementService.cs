using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Settings;
using KaizokuBackend.Utils;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KaizokuBackend.Services.Jobs
{
    /// <summary>
    /// Unified service for managing both scheduled and queued jobs
    /// </summary>
    public class JobManagementService
    {
        private readonly AppDbContext _db;
        private readonly JobsSettings _settings;
        private readonly JobHubReportService _reportService;
        private readonly ILogger<JobManagementService> _logger;
        private static readonly AsyncLock _lock = new AsyncLock();

        public JobManagementService(AppDbContext db, JobsSettings settings, JobHubReportService reportService, 
            ILogger<JobManagementService> logger)
        {
            _db = db;
            _settings = settings;
            _reportService = reportService;
            _logger = logger;
        }

        #region Recurring Jobs (previously JobSchedulerService)

        public async Task<Guid> ScheduleRecurringJobAsync<T>(JobType jobType, T parameters, 
            string? key = null, string? groupKey = null, bool runNow = false, 
            TimeSpan? schedule = null, Priority priority = Priority.Normal, 
            CancellationToken token = default)
        {
            string parametersJson = JsonSerializer.Serialize(parameters);
            return await ScheduleRecurringJobAsync(jobType, parametersJson, key, groupKey, runNow, schedule, priority, token);
        }

        public async Task SetRecurringTimeAsync(JobType jobType, TimeSpan time, CancellationToken token = default)
        {
            await _db.Jobs.Where(j => j.JobType == jobType)
                .ExecuteUpdateAsync(updates => updates.SetProperty(j => j.TimeBetweenJobs, time),token).ConfigureAwait(false);
        }
        public async Task<Guid> ScheduleRecurringJobAsync(JobType jobType, string? parametersJson, 
            string? key = null, string? groupKey = null, bool runNow = false, 
            TimeSpan? schedule = null, Priority priority = Priority.Normal, 
            CancellationToken token = default)
        {
            if (key == null)
                key = $"{jobType}";
            if (groupKey == null)
                groupKey = $"{jobType}";

            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Key == key, token).ConfigureAwait(false);
            bool added = false;
            bool changed = false;

            if (job == null)
            {
                added = true;
                job = new Job
                {
                    Id = Guid.NewGuid(),
                    JobType = jobType,
                    JobParameters = parametersJson ?? "",
                    Key = key,
                    GroupKey = groupKey,
                    Priority = priority,
                    PreviousExecution = null,
                    IsEnabled = true,
                    TimeBetweenJobs = TimeSpan.MinValue,
                };
                _db.Jobs.Add(job);
            }

            if (added || !job.IsEnabled)
            {
                if (_settings.JobTimes.TryGetValue(jobType, out TimeSpan value))
                {
                    int maxMinutes = (int)value.TotalMinutes;
                    if (maxMinutes != (int)job.TimeBetweenJobs.TotalMinutes || (!job.IsEnabled && runNow))
                    {
                        List<int> workingMinutes = _db.Jobs
                            .Where(a => a.Id != job.Id && a.JobType == jobType && a.IsEnabled)
                            .Select(a => a.MinutePlace).ToList();
                        DateTime now = DateTime.UtcNow;
                        int currentMinutes = (int)now.TimeOfDay.TotalMinutes;
                        int baseMinutes = currentMinutes - (currentMinutes % maxMinutes);
                        int minutePlace = workingMinutes.LeastUsedInteger(maxMinutes);
                        baseMinutes += minutePlace;
                        DateTime nextTime = now.Date.AddMinutes(baseMinutes);
                        if (runNow && nextTime > now)
                        {
                            nextTime -= TimeSpan.FromMinutes(maxMinutes);
                        }
                        else if (nextTime <= now && !runNow)
                        {
                            nextTime += TimeSpan.FromMinutes(maxMinutes);
                        }
                        job.TimeBetweenJobs = _settings.JobTimes[jobType];
                        job.MinutePlace = minutePlace;
                        job.NextExecution = nextTime;
                    }
                }
                else if (schedule != null)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime today = now.Date;
                    today += schedule.Value;
                    if (today < now)
                        today += TimeSpan.FromDays(1);
                    job.TimeBetweenJobs = schedule.Value;
                    job.NextExecution = today;
                }
                job.IsEnabled = true;
                changed = true;
            }

            if (added || changed)
                await _db.SaveChangesAsync(token).ConfigureAwait(false);

            if (added)
            {
                _logger.LogInformation("Added scheduled job {Key}", job.Key);
            }
            else if (changed)
            {
                _logger.LogInformation("Updated scheduled job {Key}", job.Key);
            }

            return job.Id;
        }

        public async Task<bool> EnableRecurringJobAsync(JobType jobType, string key, CancellationToken token = default)
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobType == jobType && j.Key == key, token).ConfigureAwait(false);
            if (job != null)
            {
                bool wasEnabled = job.IsEnabled;
                job.IsEnabled = true;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return wasEnabled;
            }
            return false;
        }

        public async Task<bool> DisableRecurringJobAsync(JobType jobType, string key, CancellationToken token = default)
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobType == jobType && j.Key == key, token).ConfigureAwait(false);
            if (job != null)
            {
                bool wasEnabled = job.IsEnabled;
                job.IsEnabled = false;
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return wasEnabled;
            }
            return false;
        }

        public async Task<bool> DeleteRecurringJobAsync(JobType jobType, string key, CancellationToken token = default)
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobType == jobType && j.Key == key, token).ConfigureAwait(false);
            if (job != null)
            {
                _db.Jobs.Remove(job);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        public async Task<bool?> GetRecurringJobStatusAsync(JobType jobType, string key, CancellationToken token = default)
        {
            Job? job = await _db.Jobs.Where(j => j.JobType == jobType && j.Key == key).AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            return job?.IsEnabled;
        }

        public async Task<List<Job>> GetRecurringJobsAsync(CancellationToken token = default)
        {
            return await _db.Jobs.ToListAsync(token).ConfigureAwait(false);
        }

        public async Task<List<Job>> GetRecurringJobsByTypeAsync(JobType jobType, CancellationToken token = default)
        {
            return await _db.Jobs.Where(j => j.JobType == jobType).ToListAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Immediate Jobs (previously JobEnqueueService)

        public async Task<Guid> EnqueueJobAsync<T>(JobType jobType, T parameters, 
            Priority priority = Priority.Normal, string? key = null, string? groupKey = null, 
            string? extraKey = null, string queue = "Default", CancellationToken token = default)
        {
            string parametersJson = JsonSerializer.Serialize(parameters);
            return await EnqueueJobAsIsAsync(jobType, parametersJson, priority, key, groupKey, extraKey, queue, token);
        }


        public async Task<Guid> EnqueueJobAsIsAsync(JobType jobType, string parametersJson, 
            Priority priority = Priority.Normal, string? key = null, string? groupKey = null, 
            string? extraKey = null, string queue = "Default", CancellationToken token = default)
        {
            using (await _lock.LockAsync(token))
            {
                if (string.IsNullOrEmpty(key))
                {
                    key = $"{jobType}";
                }
                else
                {
                    key = $"{jobType}_{key}";
                }
                if (groupKey == null)
                {
                    groupKey = $"{jobType}";
                }
                if (extraKey == null)
                {
                    extraKey = $"{key}";
                }

                try
                {
                    // Check for duplicate jobs with the same key that are waiting or running
                    var existingJob = _db.Queues.FirstOrDefault(j => j.Key == key);

                    if (existingJob != null)
                    {
                        if (existingJob.Status == QueueStatus.Completed || existingJob.Status == QueueStatus.Failed)
                        {
                            QueueStatus oldStatus = existingJob.Status;
                            existingJob.JobType = jobType;
                            existingJob.JobParameters = parametersJson ?? "";
                            existingJob.Queue = queue;
                            existingJob.GroupKey = groupKey;
                            existingJob.ExtraKey = extraKey;
                            existingJob.Key = key;
                            existingJob.Status = QueueStatus.Waiting;
                            existingJob.Priority = priority;
                            existingJob.EnqueuedDate = DateTime.UtcNow;
                            existingJob.ScheduledDate = DateTime.UtcNow;
                            existingJob.RetryCount = 0;
                            await UpdateJobAsync(existingJob, token).ConfigureAwait(false);
                            _logger.LogInformation("Job with key {key} with status {oldStatus}. Set to Reprocessing...", key, oldStatus);
                            return existingJob.Id;
                        }
                        _logger.LogInformation("Job with key {key} already running or waiting. Skipping...", key);
                        return existingJob.Id;
                    }

                    // Create a new job entry
                    var job = new Enqueue
                    {
                        Id = Guid.NewGuid(),
                        JobType = jobType,
                        JobParameters = parametersJson,
                        Queue = queue,
                        GroupKey = groupKey,
                        ExtraKey = extraKey,
                        Key = key,
                        Status = QueueStatus.Waiting,
                        Priority = priority,
                        EnqueuedDate = DateTime.UtcNow,
                        ScheduledDate = DateTime.UtcNow,
                        RetryCount = 0
                    };
                    await AddJobAsync(job, token).ConfigureAwait(false);
                    _logger.LogInformation("Queued job {Key} in queue {Queue} with priority {Priority}", job.Key, queue, priority);
                    return job.Id;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to queue job.");
                    throw;
                }
            }
        }

        public async Task<Guid> ScheduleJobAsync<T>(JobType jobType, T parameters, DateTime scheduledTime,
            string queue = "Default", string? key = null, string? groupKey = null, 
            string? extraKey = null, Priority priority = Priority.Normal, int retryCount = 0,
            CancellationToken token = default)
        {
            using (await _lock.LockAsync(token))
            {
                string parametersJson = JsonSerializer.Serialize(parameters);

                if (string.IsNullOrEmpty(key))
                {
                    key = $"{jobType}";
                }
                else
                {
                    key = $"{jobType}_{key}";
                }
                if (groupKey == null)
                {
                    groupKey = $"{jobType}";
                }
                if (extraKey == null)
                {
                    extraKey = $"{key}";
                }

                // Check for duplicate jobs with the same key that are waiting or running
                var existingJob = _db.Queues.FirstOrDefault(j => j.Key == key);

                if (existingJob != null)
                {
                    // Update the scheduled time if the job already exists
                    existingJob.ScheduledDate = scheduledTime;
                    existingJob.JobParameters = parametersJson ?? "";
                    existingJob.RetryCount = retryCount;
                    existingJob.Status = QueueStatus.Waiting;
                    await UpdateJobAsync(existingJob, token).ConfigureAwait(false);
                    _logger.LogDebug("Updated scheduled job {Key} in queue {Queue} for {scheduledTime} ({retryCount} retries).", existingJob.Key, existingJob.Queue, scheduledTime, retryCount);
                    return existingJob.Id;
                }

                // Create a new job entry
                var job = new Enqueue
                {
                    Id = Guid.NewGuid(),
                    JobType = jobType,
                    JobParameters = parametersJson,
                    Queue = queue,
                    GroupKey = groupKey,
                    ExtraKey = extraKey,
                    Key = key,
                    Status = QueueStatus.Waiting,
                    Priority = priority,
                    EnqueuedDate = DateTime.UtcNow,
                    ScheduledDate = scheduledTime,
                    RetryCount = retryCount
                };
                await AddJobAsync(job, token).ConfigureAwait(false);
                _logger.LogInformation("Scheduled job {Key} in queue {Queue} for {ScheduledTime}", job.Key, queue, scheduledTime);
                return job.Id;
            }
        }

        public async Task DeleteQueuedJobsAsync(JobType jobType, string extraKey, CancellationToken token = default)
        {
            using (await _lock.LockAsync(token))
            {
                var existingJobs = _db.Queues.Where(j => j.JobType == jobType && j.ExtraKey == extraKey);
                _db.Queues.RemoveRange(existingJobs);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }

        public async Task DeleteQueuedJobsAsync(IEnumerable<Guid> jobIds, CancellationToken token = default)
        {
            using (await _lock.LockAsync(token))
            {
                var existingJobs = _db.Queues.Where(j => jobIds.Contains(j.Id));
                _db.Queues.RemoveRange(existingJobs);
                await _db.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }

        #endregion

        #region System Operations

        public DbSet<Enqueue> QueuedJobs => _db.Queues;

        public async Task StartupAsync(CancellationToken token = default)
        {
            // Reset running jobs to waiting on startup
            await _db.Queues.Where(j => j.Status == QueueStatus.Running)
                .ExecuteUpdateAsync(a => a.SetProperty(b => b.Status, QueueStatus.Waiting), token)
                .ConfigureAwait(false);
        }

        public void DetachJob(Enqueue job)
        {
            _db.Entry(job).State = EntityState.Detached;
        }

        #endregion

        #region Private Helper Methods

        private async Task UpdateJobAsync(Enqueue job, CancellationToken token = default)
        {
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            await _reportService.ReportJobAsync(job, token).ConfigureAwait(false);
        }

        private async Task AddJobAsync(Enqueue job, CancellationToken token = default)
        {
            _db.Queues.Add(job);
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
            await _reportService.ReportJobAsync(job, token).ConfigureAwait(false);
        }

        #endregion
    }
}