using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KaizokuBackend.Services.Series;

namespace KaizokuBackend.Services.Jobs.Commands;

public class GetChapters : ICommand
{
    public JobType JobType => JobType.GetChapters;
    public Type? ParameterType => typeof(Guid);
    private readonly SeriesCommandService _seriesCommand;
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(GetChapters))]
    public GetChapters(SeriesCommandService seriesCommand)
    {
        _seriesCommand = seriesCommand;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        if (job.Parameters == null)
            return JobResult.Failed;
        Guid serviceProvider = JsonSerializer.Deserialize<Guid>(job.Parameters);
        return await _seriesCommand.DownloadSeriesAsync(serviceProvider, token).ConfigureAwait(false);
    }
}


