using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Jobs;
using KaizokuBackend.Services.Jobs.Settings;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Services.Background
{
    /// <summary>
    /// Background service that processes recurring scheduled jobs
    /// </summary>
    public class JobScheduledHostedService : IWorkerService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobScheduledHostedService> _logger;
        private readonly JobsSettings _settings;

        public JobScheduledHostedService(IServiceScopeFactory scopeFactory, ILogger<JobScheduledHostedService> logger,
            JobsSettings settings)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _settings = settings;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Job Scheduler Service is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessScheduledJobsAsync(stoppingToken).ConfigureAwait(false);
                    await Task.Delay(_settings.JobsPollingInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal cancellation, no need to log
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scheduled jobs");
                }
            }

            _logger.LogInformation("Job Scheduler Service is stopping");
        }

        private async Task ProcessScheduledJobsAsync(CancellationToken stoppingToken)
        {
            // Create a scope for database operations
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobManagementService = scope.ServiceProvider.GetRequiredService<JobManagementService>();
            
            // Get current time
            var now = DateTime.UtcNow;
            
            // Find all jobs that need to be executed
            var dueJobs = await dbContext.Jobs
                .Where(j => j.NextExecution <= now && j.IsEnabled)
                .ToListAsync(stoppingToken);
                
            if (dueJobs.Count == 0)
            {
                return;
            }

            foreach (var job in dueJobs)
            {
                try
                {
                    // Enqueue the job for immediate execution
                    await jobManagementService.EnqueueJobAsIsAsync(
                        job.JobType,
                        job.JobParameters ?? "", 
                        job.Priority,
                        job.Key, 
                        job.GroupKey,
                        job.GroupKey,
                        "Default",
                        stoppingToken).ConfigureAwait(false);
                    
                    // Update job for next execution
                    job.PreviousExecution = job.NextExecution;
                    while(job.NextExecution < DateTime.UtcNow)
                       job.NextExecution = job.NextExecution.Add(job.TimeBetweenJobs);
                    
                    await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    
                    _logger.LogInformation("Next Queued Execution of job {Key} will be {NextExecution}",
                        job.Key, job.NextExecution);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scheduled job {Key} of type {JobType}",
                        job.Key, job.JobType);
                }
            }
        }
    }
}