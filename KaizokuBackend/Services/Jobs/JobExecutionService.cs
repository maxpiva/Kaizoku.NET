using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Jobs.Models;
using System.Reflection;

namespace KaizokuBackend.Services.Jobs
{
    /// <summary>
    /// Service responsible for executing job commands
    /// </summary>
    public class JobExecutionService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobExecutionService> _logger;
        private readonly List<Type> _commandTypes;

        public JobExecutionService(IServiceScopeFactory scopeFactory, ILogger<JobExecutionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _commandTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(type => typeof(ICommand).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                .ToList();
        }

        public async Task<JobResult> ExecuteJobAsync(JobInfo jobInfo, CancellationToken token = default)
        {
            using var scope = _scopeFactory.CreateScope();
            
            try
            {
                ICommand? command = GetCommandInstance(scope.ServiceProvider, jobInfo.JobType);
                if (command == null)
                {
                    _logger.LogError("No command handler found for job type {JobType}", jobInfo.JobType);
                    return JobResult.Failed;
                }

                _logger.LogInformation("Executing {JobType} on {groupKey}", jobInfo.JobType, jobInfo.GroupKey ?? jobInfo.Key);
                JobResult result = await command.ExecuteAsync(jobInfo, token).ConfigureAwait(false);
                _logger.LogInformation("Job {JobType} on {groupKey} {Result}", jobInfo.JobType, jobInfo.GroupKey ?? jobInfo.Key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job {JobType} on {groupKey}", jobInfo.JobType, jobInfo.Key);
                return JobResult.Failed;
            }
        }

        private ICommand? GetCommandInstance(IServiceProvider serviceProvider, JobType jobType)
        {
            Type? commandType = _commandTypes.FirstOrDefault(t => t.Name == jobType.ToString());
            if (commandType == null)
                return null;

            return ActivatorUtilities.CreateInstance(serviceProvider, commandType) as ICommand;
        }
    }
}