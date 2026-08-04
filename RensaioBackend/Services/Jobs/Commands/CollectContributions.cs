using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Settings;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

public sealed class CollectContributions : ICommand
{
    public JobType JobType => JobType.CollectContributions;
    public Type? ParameterType => null;
    private readonly ContributionCollector _collector;
    private readonly SettingsService _settings;
    private readonly JobManagementService _jobs;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CollectContributions))]
    public CollectContributions(ContributionCollector collector, SettingsService settings, JobManagementService jobs)
    {
        _collector = collector;
        _settings = settings;
        _jobs = jobs;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            await _collector.RunAsync(token).ConfigureAwait(false);
            // Fresh collection results should reach the contribution DB without waiting for
            // the daily schedule; the uploader's delta store makes the chained run cheap.
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            if (settings.ContributionUploadEnabled)
            {
                await _jobs.EnqueueJobAsync(JobType.UploadContributions, (string?)null,
                    Priority.Low, nameof(JobType.UploadContributions), nameof(JobType.UploadContributions),
                    nameof(JobType.UploadContributions), "Default", token).ConfigureAwait(false);
            }
            return JobResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return JobResult.Failed;
        }
    }
}
