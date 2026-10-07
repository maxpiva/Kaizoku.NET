using RensaioBackend.Data;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Jobs;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using RensaioBackend.Extensions;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Jobs.Settings;
using RensaioBackend.Models.Enums;

namespace RensaioBackend.Services.Background
{
    public interface IWorkerService
    {
        Task ExecuteAsync(CancellationToken stoppingToken);
    }
    public class JobQueueHostedService : IWorkerService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobQueueHostedService> _logger;
        private readonly JobsSettings _settings;
        private readonly ConcurrentDictionary<JobQueues, ConcurrentDictionary<string, byte>> _runningJobs = new();
        private readonly object _slotLock = new object();
        // Tracks the in-flight job task so shutdown can drain it. Keyed by a monotonically
        // increasing id so completed tasks can be removed on every poll cycle — previously
        // this bag grew by ~2 entries/sec forever because entries were only drained at shutdown.
        private readonly ConcurrentDictionary<long, Task> _inFlightJobTasks = new();
        private long _inFlightTaskCounter = 0;

        public JobQueueHostedService(IServiceScopeFactory scopeFactory, ILogger<JobQueueHostedService> logger,
            JobsSettings settings)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _settings = settings;

            // Initialize running jobs tracking
            foreach (var queue in _settings.GetQueueSettings())
            {
                _runningJobs[queue.Name] = new ConcurrentDictionary<string, byte>();
            }
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Job Queue Service is starting");
            
            using (var scope = _scopeFactory.CreateScope())
            {
                var jobManagement = scope.ServiceProvider.GetRequiredService<JobManagementService>();
                await jobManagement.StartupAsync(stoppingToken).ConfigureAwait(false);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessJobQueuesAsync(stoppingToken).ConfigureAwait(false);
                    await Task.Delay(_settings.QueuePollingInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job queues");
                }
            }

            _logger.LogInformation("Job Queue Service is stopping. Waiting for {Count} in-flight jobs to complete...", _inFlightJobTasks.Count);
            
            // Drain in-flight jobs with a bounded timeout to prevent shutdown hang
            try
            {
                await Task.WhenAll(_inFlightJobTasks.Values).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                _logger.LogInformation("All in-flight jobs completed successfully.");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Some in-flight jobs did not complete within the 30-second shutdown timeout.");
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown - some jobs were cancelled
            }
        }

        private async Task ProcessJobQueuesAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var jobManagement = scope.ServiceProvider.GetRequiredService<JobManagementService>();

            foreach (var queueEntry in _settings.GetQueueSettings())
            {
                await ProcessQueueAsync(jobManagement, queueEntry, stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task ProcessQueueAsync(JobManagementService jobManagement, QueueSettings queueSettings, 
            CancellationToken stoppingToken)
        {
            var queueName = queueSettings.Name;
            var runningJobsInQueue = _runningJobs.GetOrAdd(queueName, _ => new ConcurrentDictionary<string, byte>());

            // Check available slots outside lock first (quick exit optimization)
            if (runningJobsInQueue.Count >= queueSettings.MaxThreads)
                return;

            var availableSlots = queueSettings.MaxThreads - runningJobsInQueue.Count;
            if (availableSlots <= 0)
                return;

            // Get jobs ready for execution
            var jobsToProcess = await GetJobsToProcessAsync(jobManagement, queueName, queueSettings, 
                availableSlots, stoppingToken).ConfigureAwait(false);

            foreach (var job in jobsToProcess)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                // Atomic slot allocation: check and reserve under lock
                lock (_slotLock)
                {
                    if (runningJobsInQueue.Count >= queueSettings.MaxThreads)
                        break;

                    if (!runningJobsInQueue.TryAdd(job.Id.ToString(), 0))
                        continue; // Job already running, skip
                }

                // Update job status to running
                job.Status = QueueStatus.Running;
                job.StartedDate = DateTime.UtcNow;
                
                // Save changes through the service
                await UpdateJobStatusAsync(job, stoppingToken).ConfigureAwait(false);
                jobManagement.DetachJob(job);
                
                // Track in-flight job task so we can drain on shutdown. Completed tasks are
                // removed immediately so the dictionary stays bounded (it previously grew
                // by ~2 entries/sec for the process lifetime).
                var jobTask = ExecuteJobAsync(job, queueName, queueSettings, stoppingToken);
                long taskId = Interlocked.Increment(ref _inFlightTaskCounter);
                _inFlightJobTasks[taskId] = jobTask;
                jobTask.ContinueWith(_ => _inFlightJobTasks.TryRemove(taskId, out _));
            }
        }

        private async Task<List<EnqueueEntity>> GetJobsToProcessAsync(JobManagementService jobManagement, JobQueues queueName,
            QueueSettings queueSettings, int availableSlots, CancellationToken stoppingToken)
        {
            // Get running job counts by group
            var runningCounts = await jobManagement.QueuedJobs
                .Where(a => a.Status == QueueStatus.Running)
                .GroupBy(a => a.GroupKey)
                .ToDictionaryAsync(a => a.Key, a => a.Count(), stoppingToken);

            // Get waiting jobs for this queue
            var waitingJobs = await jobManagement.QueuedJobs
                .Where(j => j.Queue == queueName.ToString() && 
                           j.Status == QueueStatus.Waiting && 
                           j.ScheduledDate <= DateTime.UtcNow)
                .OrderByDescending(j => j.Priority)
                .ToListAsync(stoppingToken);

            // Apply group limits and fair sharing
            var jobsByPriority = waitingJobs.GroupBy(j => j.Priority).ToDictionary(g => g.Key, g => g.ToList());

            foreach (Priority priority in jobsByPriority.Keys)
            {
                var groupedJobs = jobsByPriority[priority]
                    .GroupBy(a => a.GroupKey)
                    .ToDictionary(g => g.Key, g => g.Take(runningCounts.GetLocalGroupMax(g.Key, queueSettings.MaxPerGroup)).ToList());
                
                jobsByPriority[priority] = groupedJobs.SelectMany(a => a.Value).FairShareOrderBy(a => a.GroupKey).ToList();
            }

            return jobsByPriority.SelectMany(a => a.Value).Take(availableSlots).ToList();
        }

        private async Task UpdateJobStatusAsync(EnqueueEntity job, CancellationToken stoppingToken)
        {
            // Update job status through database context
            using var scope = _scopeFactory.CreateScope();
            var management = scope.ServiceProvider.GetRequiredService<JobManagementService>();
            // This uses the internal update method
            await management.QueuedJobs.Where(j => j.Id == job.Id)
                .ExecuteUpdateAsync(updates => updates.SetProperty(j => j.Status, QueueStatus.Running)
                    .SetProperty(j => j.StartedDate, DateTime.UtcNow), stoppingToken);
        }

        private async Task ExecuteJobAsync(EnqueueEntity job, JobQueues queueName, QueueSettings queueSettings,
            CancellationToken stoppingToken)
        {
            var jobId = job.Id.ToString();
            
            using var scope = _scopeFactory.CreateScope();
            var jobExecution = scope.ServiceProvider.GetRequiredService<JobExecutionService>();
            
            try
            {
                _logger.LogDebug("Executing job {JobType} {Key} in queue {queueName}", job.JobType, job.Key, queueName);

                JobInfo jobInfo = new JobInfo(job.Id, job.JobType, job.Key, job.GroupKey, job.JobParameters);
                JobResult result = await jobExecution.ExecuteJobAsync(jobInfo, stoppingToken).ConfigureAwait(false);
                
                await HandleJobResultAsync(job, result, queueName, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown — job was cancelled, no need to log as error
            }
            catch (ObjectDisposedException)
            {
                // DI container is being disposed during shutdown — job scope creation failed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job {Key} in queue {queueName}", job.Key, queueName);
                // Attempt graceful failure handling; if it fails due to shutdown, just swallow
                try
                {
                    await HandleJobFailureAsync(job, queueSettings, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
            finally
            {
                // Remove job from running list
                if (_runningJobs.TryGetValue(queueName, out var runningJobs))
                {
                    runningJobs.TryRemove(jobId, out _);
                }
            }
        }

        private async Task HandleJobResultAsync(EnqueueEntity job, JobResult result, JobQueues queueName,
            CancellationToken stoppingToken)
        {
            // Guard against disposed container during shutdown
            if (stoppingToken.IsCancellationRequested)
                return;

            using var scope = _scopeFactory.CreateScope();
            var management = scope.ServiceProvider.GetRequiredService<JobManagementService>();
            
            if (result != JobResult.Handled)
            {
                var updatedJob = await management.QueuedJobs.FirstOrDefaultAsync(a => a.Id == job.Id, stoppingToken);
                if (updatedJob != null)
                {
                    updatedJob.Status = result == JobResult.Success ? QueueStatus.Completed : QueueStatus.Failed;
                    updatedJob.FinishedDate = DateTime.UtcNow;
                    if (result == JobResult.Success)
                    {
                        _logger.LogDebug("Completed job {Key} in queue {queueName}", job.Key, queueName);
                    }
                    else
                    {
                        _logger.LogDebug("Failed job {Key} in queue {queueName}", job.Key, queueName);
                    }
                    await management.QueuedJobs.Where(j => j.Id == job.Id)
                        .ExecuteUpdateAsync(updates => updates.SetProperty(j => j.Status, updatedJob.Status)
                            .SetProperty(j => j.FinishedDate, updatedJob.FinishedDate), stoppingToken);
                            
                    if (result == JobResult.Delete)
                    {
                        // Extract the recurring job key from the enqueued job's key.
                        // The enqueued job key is formatted as "{JobType}_{key}" by EnqueueJobAsIsAsync.
                        // The recurring job key is the suffix after "{JobType}_".
                        // e.g., enqueued key "GetChapters_d0e179a7-1814..." -> recurring key "d0e179a7-1814..."
                        string prefix = $"{job.JobType}_";
                        string recurringKey = job.Key.StartsWith(prefix)
                            ? job.Key.Substring(prefix.Length)
                            : job.Key;
                        await management.DeleteRecurringJobAsync(job.JobType, recurringKey, stoppingToken);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Rescheduled job {jobType} {GroupKey} in queue {queueName}", job.JobType, job.GroupKey ?? job.Key, queueName);
            }
        }

        private async Task HandleJobFailureAsync(EnqueueEntity job, QueueSettings queueSettings, CancellationToken stoppingToken)
        {
            // Guard against disposed container during shutdown
            if (stoppingToken.IsCancellationRequested)
                return;

            using var scope = _scopeFactory.CreateScope();
            var management = scope.ServiceProvider.GetRequiredService<JobManagementService>();
            
            var updatedJob = await management.QueuedJobs.FirstOrDefaultAsync(a => a.Id == job.Id, stoppingToken);
            if (updatedJob != null)
            {
                updatedJob.RetryCount += 1;
                
                if (updatedJob.RetryCount >= queueSettings.MaxRetries)
                {
                    updatedJob.Status = QueueStatus.Failed;
                    updatedJob.FinishedDate = DateTime.UtcNow;
                }
                else
                {
                    updatedJob.Status = QueueStatus.Waiting;
                    updatedJob.ScheduledDate = DateTime.UtcNow.Add(queueSettings.RetryTimeSpan);
                }
                
                await management.QueuedJobs.Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(updates => updates.SetProperty(j => j.Status, updatedJob.Status)
                        .SetProperty(j => j.RetryCount, updatedJob.RetryCount)
                        .SetProperty(j => j.FinishedDate, updatedJob.FinishedDate)
                        .SetProperty(j => j.ScheduledDate, updatedJob.ScheduledDate), stoppingToken);
            }
        }
    }
}