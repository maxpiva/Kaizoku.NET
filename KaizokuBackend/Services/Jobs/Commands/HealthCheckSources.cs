using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Providers;
using System.Diagnostics.CodeAnalysis;

namespace KaizokuBackend.Services.Jobs.Commands;

public class HealthCheckSources : ICommand
{
    public JobType JobType => JobType.HealthCheckSources;
    public Type? ParameterType => null;

    private readonly ProviderHealthCheckService _healthCheck;
    private readonly ILogger<HealthCheckSources> _logger;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(HealthCheckSources))]
    public HealthCheckSources(ProviderHealthCheckService healthCheck, ILogger<HealthCheckSources> logger)
    {
        _healthCheck = healthCheck;
        _logger = logger;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            _logger.LogInformation("Running scheduled health check for all sources...");
            var results = await _healthCheck.CheckAllProvidersAsync(token).ConfigureAwait(false);

            int passed = results.Count(r => r.Passed);
            int failed = results.Count(r => !r.Passed);
            _logger.LogInformation("Health check complete: {Passed} passed, {Failed} failed out of {Total} sources.",
                passed, failed, results.Count);

            return JobResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running scheduled health check");
            return JobResult.Failed;
        }
    }
}
