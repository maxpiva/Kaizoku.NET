using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;

namespace KaizokuBackend.Services.Jobs.Report;

public class ProgressReporter
{
    private readonly IReportProgress _report;
    public IProgress<ProgressState> Progress { get; }
    public JobInfo Job { get; }
    public ProgressReporter(IReportProgress report, JobInfo job)
    {
        _report = report;
        Job = job;
        Progress = new Progress<ProgressState>(async state =>
        {
            await _report.ReportProgressAsync(state).ConfigureAwait(false);
        });
    }    
    public void Report(ProgressStatus status, decimal percentage,string? message, object? parameter = null, string? errorMessage = null)
    {
        Progress.Report(new ProgressState
        {
            Id = Job.JobId,
            JobType = Job.JobType,
            ProgressStatus = status,
            Percentage = percentage,
            Message = message ?? "",
            Parameter = parameter,
            ErrorMessage = errorMessage
        });
    }

}