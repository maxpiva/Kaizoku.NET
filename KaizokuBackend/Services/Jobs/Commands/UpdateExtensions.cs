using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Providers;
using System.Diagnostics.CodeAnalysis;

namespace KaizokuBackend.Services.Jobs.Commands;

public class UpdateExtensions : ICommand
{
    public JobType JobType => JobType.UpdateExtensions;
    public Type? ParameterType => null;

    private readonly ProviderCacheService _cache;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(UpdateExtensions))]

    public UpdateExtensions(ProviderCacheService cache)
    {
        _cache = cache;
    }
    
    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            // Get all cached providers and check for updates
            await _cache.RefreshCacheAsync(token).ConfigureAwait(false);
            return JobResult.Success;
        }
        catch (Exception)
        {
            return JobResult.Failed;
        }
    }
}