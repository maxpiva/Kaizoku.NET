using KaizokuBackend.Models.Enums;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Jobs.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace KaizokuBackend.Services.Jobs.Commands;

public class ScanLocalFiles : ICommand
{
    public JobType JobType => JobType.ScanLocalFiles;
    public Type? ParameterType => typeof(string);
    private readonly ImportCommandService _service;
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ScanLocalFiles))]
    public ScanLocalFiles(ImportCommandService service)
    {
        _service = service;
    }

    public async Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default)
    {
        if (job.Parameters == null)
            return JobResult.Failed;
        string? path = JsonSerializer.Deserialize<string>(job.Parameters);
        if (path == null)
            return JobResult.Failed;
        return await _service.ScanAsync(path, job, token).ConfigureAwait(false);
    }
}