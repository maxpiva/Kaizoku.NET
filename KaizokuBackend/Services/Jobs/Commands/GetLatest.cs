using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using KaizokuBackend.Services.Series;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Jobs.Commands;

public class GetLatest : ICommand
{
    public JobType JobType => JobType.GetLatest;
    public Type? ParameterType => typeof(string);
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
        string? mihonprovideId = JsonSerializer.Deserialize<string>(job.Parameters);
        if (mihonprovideId == null)
            return JobResult.Failed;
        return await _seriesCommand.UpdateSourceAsync(mihonprovideId, token).ConfigureAwait(false);
    }
}