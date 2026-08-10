using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Providers;
using RensaioBackend.Services.Settings;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

public class UpdateExtensions : ICommand
{
    public JobType JobType => JobType.UpdateExtensions;
    public Type? ParameterType => null;

    private readonly ProviderCacheService _cache;
    private readonly JobBusinessService _jobBusiness;
    private readonly SettingsService _settings;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(UpdateExtensions))]

    public UpdateExtensions(ProviderCacheService cache, JobBusinessService jobBusiness, SettingsService settings)
    {
        _cache = cache;
        _jobBusiness = jobBusiness;
        _settings = settings;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            // Get all cached providers and check for updates
            await _cache.UpdateAllExtensionsAsync(token).ConfigureAwait(false);

            // The extension index may have changed; nudge the discovery precache job so new
            // eligible extensions get their artifacts prepared. The job itself no-ops cheaply
            // when the eligible set is unchanged.
            var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (settings.DiscoveryIncludeInSearch && settings.DiscoveryPrecacheEnabled)
            {
                await _jobBusiness.ManageDiscoveryPrecacheAsync(true, runNow: true, token).ConfigureAwait(false);
            }
            return JobResult.Success;
        }
        catch (Exception)
        {
            return JobResult.Failed;
        }
    }
}