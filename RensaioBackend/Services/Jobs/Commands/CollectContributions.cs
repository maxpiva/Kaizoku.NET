using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

public sealed class CollectContributions : ICommand
{
    public JobType JobType => JobType.CollectContributions;
    public Type? ParameterType => null;
    private readonly ContributionCollector _collector;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CollectContributions))]
    public CollectContributions(ContributionCollector collector)
    {
        _collector = collector;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            await _collector.RunAsync(token).ConfigureAwait(false);
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
