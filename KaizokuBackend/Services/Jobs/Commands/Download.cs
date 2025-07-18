using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Downloads;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace KaizokuBackend.Services.Jobs.Commands;

public class Download : ICommand
{
    public JobType JobType => JobType.Download;
    public Type? ParameterType => typeof(ChapterDownload);
    private readonly DownloadCommandService _downloadCommand;
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(Download))]
    public Download(DownloadCommandService downloadCommand)
    {
        _downloadCommand = downloadCommand;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        if (job.Parameters == null)
            return JobResult.Failed;
        ChapterDownload? chap = JsonSerializer.Deserialize<ChapterDownload>(job.Parameters);
        if (chap == null)
            return JobResult.Failed;
        return await _downloadCommand.DownloadChapterAsync(chap, job, token).ConfigureAwait(false);
    }
}