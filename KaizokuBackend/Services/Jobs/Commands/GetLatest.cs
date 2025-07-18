using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KaizokuBackend.Services.Series;

namespace KaizokuBackend.Services.Jobs.Commands;

public class GetLatest : ICommand
{
    public JobType JobType => JobType.GetLatest;
    public Type? ParameterType => typeof(SuwayomiSource);
    private readonly SeriesCommandService _seriesCommand;
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(GetLatest))]
    public GetLatest(SeriesCommandService seriesCommand)
    {
        _seriesCommand = seriesCommand;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        if (job.Parameters == null)
            return JobResult.Failed;
        SuwayomiSource? source = JsonSerializer.Deserialize<SuwayomiSource>(job.Parameters);
        if (source == null)
            return JobResult.Failed;
        return await _seriesCommand.UpdateSourceAsync(source, token).ConfigureAwait(false);
    }
}