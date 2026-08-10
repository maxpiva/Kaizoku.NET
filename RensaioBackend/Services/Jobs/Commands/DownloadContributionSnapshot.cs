using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Contributions.Snapshot;
using RensaioBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

public sealed class DownloadContributionSnapshot : ICommand
{
    public JobType JobType => JobType.DownloadContributionSnapshot;
    public Type? ParameterType => null;
    private readonly ContributionSnapshotDownloader _downloader;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(DownloadContributionSnapshot))]
    public DownloadContributionSnapshot(ContributionSnapshotDownloader downloader)
    {
        _downloader = downloader;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            await _downloader.RunAsync(token).ConfigureAwait(false);
            // Expected failures (unreachable export, key/export mismatch, malformed required files)
            // are modeled as a Failed state rather than thrown, so surface them as a failed job.
            ContributionSnapshotStatusDto status = await _downloader.GetStatusAsync(token).ConfigureAwait(false);
            return status.State == ContributionSnapshotStates.Failed ? JobResult.Failed : JobResult.Success;
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
