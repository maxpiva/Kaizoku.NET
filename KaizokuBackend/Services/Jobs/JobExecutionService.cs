using KaizokuBackend.Models;
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

        private static readonly Lazy<Dictionary<string, Type>> _commandTypeMap = new(() =>
            Assembly.GetExecutingAssembly().GetTypes()
                .Where(type => typeof(ICommand).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                .ToDictionary(type => type.Name, type => type));

        public JobExecutionService(IServiceScopeFactory scopeFactory, ILogger<JobExecutionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
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

                _logger.LogInformation("Executing job {Key} of type {JobType}", jobInfo.Key, jobInfo.JobType);
                JobResult result = await command.ExecuteAsync(jobInfo, token).ConfigureAwait(false);
                
                _logger.LogInformation("Job {Key} completed with result {Result}", jobInfo.Key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job {Key} of type {JobType}", jobInfo.Key, jobInfo.JobType);
                return JobResult.Failed;
            }
        }

        private ICommand? GetCommandInstance(IServiceProvider serviceProvider, JobType jobType)
        {
            string commandName = jobType.ToString();
            if (!_commandTypeMap.Value.TryGetValue(commandName, out Type? commandType))
            {
                _logger.LogError(
                    "Command type '{CommandName}' not found. Available commands: {AvailableCommands}",
                    commandName,
                    string.Join(", ", _commandTypeMap.Value.Keys));
                return null;
            }

            return ActivatorUtilities.CreateInstance(serviceProvider, commandType) as ICommand;
        }
    }
}