using Microsoft.AspNetCore.Mvc;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Contributions.Snapshot;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Settings;

namespace RensaioBackend.Controllers;

[ApiController]
[Route("api/contributions")]
[Produces("application/json")]
[RequireUserLevel(UserLevel.Owner)]
public sealed class ContributionsController : ControllerBase
{
    private readonly ContributionCollector _collector;
    private readonly ContributionUploader _uploader;
    private readonly ContributionSnapshotDownloader _snapshot;
    private readonly SettingsService _settings;
    private readonly JobManagementService _jobs;

    public ContributionsController(
        ContributionCollector collector,
        ContributionUploader uploader,
        ContributionSnapshotDownloader snapshot,
        SettingsService settings,
        JobManagementService jobs)
    {
        _collector = collector;
        _uploader = uploader;
        _snapshot = snapshot;
        _settings = settings;
        _jobs = jobs;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ContributionStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ContributionStatusDto>> GetStatusAsync(CancellationToken token = default)
    {
        ContributionStatusDto status = await _collector.GetStatusAsync(token).ConfigureAwait(false);
        status.Upload = await _uploader.GetStatusAsync(token).ConfigureAwait(false);
        status.Snapshot = await _snapshot.GetStatusAsync(token).ConfigureAwait(false);
        return Ok(status);
    }

    [HttpPost("run")]
    [ProducesResponseType(typeof(ContributionStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContributionStatusDto>> RunAsync(CancellationToken token = default)
    {
        var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        if (!settings.ContributionCollectorEnabled)
            return Conflict(new { error = "Contribution collector is disabled." });

        // Enqueue first: if enqueueing throws, the error propagates without a phantom
        // "queued" status having been persisted.
        await _jobs.EnqueueJobAsync(JobType.CollectContributions, (string?)null,
            Priority.Low, nameof(JobType.CollectContributions), nameof(JobType.CollectContributions),
            nameof(JobType.CollectContributions), "Default", token).ConfigureAwait(false);
        await _collector.MarkQueuedAsync(token).ConfigureAwait(false);
        return Accepted(await _collector.GetStatusAsync(token).ConfigureAwait(false));
    }

    [HttpPost("upload/run")]
    [ProducesResponseType(typeof(ContributionUploadStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContributionUploadStatusDto>> RunUploadAsync(CancellationToken token = default)
    {
        var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        if (!settings.ContributionUploadEnabled)
            return Conflict(new { error = "Contribution upload is disabled." });

        await _jobs.EnqueueJobAsync(JobType.UploadContributions, (string?)null,
            Priority.Low, nameof(JobType.UploadContributions), nameof(JobType.UploadContributions),
            nameof(JobType.UploadContributions), "Default", token).ConfigureAwait(false);
        await _uploader.MarkQueuedAsync(token).ConfigureAwait(false);
        return Accepted(await _uploader.GetStatusAsync(token).ConfigureAwait(false));
    }

    [HttpPost("snapshot/run")]
    [ProducesResponseType(typeof(ContributionSnapshotStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContributionSnapshotStatusDto>> RunSnapshotAsync(CancellationToken token = default)
    {
        var settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        if (!settings.ContributionSnapshotEnabled)
            return Conflict(new { error = "Contribution snapshot download is disabled." });

        await _jobs.EnqueueJobAsync(JobType.DownloadContributionSnapshot, (string?)null,
            Priority.Low, nameof(JobType.DownloadContributionSnapshot), nameof(JobType.DownloadContributionSnapshot),
            nameof(JobType.DownloadContributionSnapshot), "Default", token).ConfigureAwait(false);
        await _snapshot.MarkQueuedAsync(token).ConfigureAwait(false);
        return Accepted(await _snapshot.GetStatusAsync(token).ConfigureAwait(false));
    }

    [HttpPost("upload/validate")]
    [ProducesResponseType(typeof(ContributionContributorDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ContributionContributorDto>> ValidateContributorAsync(CancellationToken token = default)
        => Ok(await _uploader.ValidateContributorAsync(token).ConfigureAwait(false));
}
