using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Dto;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Jobs.Models;

namespace RensaioBackend.Services.Jobs.Report;

public class ProgressReporter
{
    private readonly JobHubReportService _report;
    public IProgress<ProgressState> Progress { get; }
    public JobInfo Job { get; }
    public ProgressReporter(JobHubReportService report, JobInfo job)
    {
        _report = report;
        Job = job;
        Progress = new Progress<ProgressState>(async state =>
        {
            await _report.ReportProgressAsync(state).ConfigureAwait(false);
        });
    }    
    public async Task ReportAsync(ProgressStatus status, decimal percentage,string? message, DownloadSummary? download = null, string? errorMessage = null, CancellationToken token = default)
    {
        ProgressState state = new ProgressState
        {
            Id = Job.JobId,
            JobType = Job.JobType,
            ProgressStatus = status,
            Percentage = percentage,
            Message = message ?? "",
            ErrorMessage = errorMessage,
            Download = download?.ToCardInfoDto()
        };
        if (state.Download!=null)
            await _report.ThumbService.PopulateThumbsAsync(state.Download, token: token);
        Progress.Report(state);
    }

}