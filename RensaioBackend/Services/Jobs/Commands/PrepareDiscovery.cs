using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Jobs.Models;
using RensaioBackend.Services.Search;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

/// <summary>
/// Low-priority background job that pre-downloads and dex2jar-converts the discovery artifacts of
/// every eligible not-installed extension (languages + NSFW filtered, capped by the discovery cap
/// setting). No classloading happens here — artifacts only — so the job costs disk and one globally
/// serialized converter, never memory. It no-ops when the eligible set hasn't changed since the
/// last clean run.
/// </summary>
public class PrepareDiscovery : ICommand
{
    public JobType JobType => JobType.PrepareDiscovery;
    public Type? ParameterType => null;

    private readonly DiscoverySearchService _discovery;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(PrepareDiscovery))]
    public PrepareDiscovery(DiscoverySearchService discovery)
    {
        _discovery = discovery;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            await _discovery.PrepareEligibleArtifactsAsync(token).ConfigureAwait(false);
            return JobResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return JobResult.Failed;
        }
    }
}
