using KaizokuBackend.Models;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;

namespace KaizokuBackend.Services.Jobs.Commands;

public class SearchProviders : ICommand
{
    public JobType JobType => JobType.SearchProviders;
    public Type? ParameterType => null;
    private readonly ImportCommandService _service;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SearchProviders))]
    public SearchProviders(ImportCommandService service)
    {
        _service = service;
    }
    public Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        return _service.SearchSeriesAsync(job, token);
    }
}