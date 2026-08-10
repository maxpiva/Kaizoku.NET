using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;

namespace RensaioBackend.Services.Jobs.Commands;

public sealed class UploadContributions : ICommand
{
    public JobType JobType => JobType.UploadContributions;
    public Type? ParameterType => null;
    private readonly ContributionUploader _uploader;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(UploadContributions))]
    public UploadContributions(ContributionUploader uploader)
    {
        _uploader = uploader;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        try
        {
            await _uploader.RunAsync(token).ConfigureAwait(false);
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
