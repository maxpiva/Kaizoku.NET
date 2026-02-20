using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Jobs.Models;

public interface ICommand
{
    public JobType JobType { get; }
    public Type? ParameterType { get; }
    public Task<JobResult> ExecuteAsync(JobInfo job, CancellationToken token = default);
}