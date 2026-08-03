using Microsoft.AspNetCore.Mvc;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Auth;
using RensaioBackend.Services.Contributions;
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
    private readonly SettingsService _settings;
    private readonly JobManagementService _jobs;

    public ContributionsController(
        ContributionCollector collector,
        SettingsService settings,
        JobManagementService jobs)
    {
        _collector = collector;
        _settings = settings;
        _jobs = jobs;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ContributionStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ContributionStatusDto>> GetStatusAsync(CancellationToken token = default)
        => Ok(await _collector.GetStatusAsync(token).ConfigureAwait(false));

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
}
