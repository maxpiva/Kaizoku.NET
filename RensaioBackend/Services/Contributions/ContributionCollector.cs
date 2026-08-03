using Mihon.ExtensionsBridge.Models;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Bridge;
using RensaioBackend.Services.Search.Discovery;
using RensaioBackend.Services.Settings;

namespace RensaioBackend.Services.Contributions;

public sealed class ContributionCollector
{
    private readonly MihonBridgeService _mihon;
    private readonly SettingsService _settings;
    private readonly IContributionWorkerController _workers;
    private readonly IContributionSink _sink;
    private readonly IContributionCheckpointStore _checkpoints;
    private readonly InteractiveDiscoveryGate _interactive;
    private readonly ILogger<ContributionCollector> _logger;
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    public ContributionCollector(
        MihonBridgeService mihon,
        SettingsService settings,
        IContributionWorkerController workers,
        IContributionSink sink,
        IContributionCheckpointStore checkpoints,
        InteractiveDiscoveryGate interactive,
        ILogger<ContributionCollector> logger)
    {
        _mihon = mihon;
        _settings = settings;
        _workers = workers;
        _sink = sink;
        _checkpoints = checkpoints;
        _interactive = interactive;
        _logger = logger;
    }

    public async Task<ContributionStatusDto> GetStatusAsync(CancellationToken token = default)
    {
        EditableSettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        ContributionCheckpointV1 checkpoint = await _checkpoints.ReadAsync(token).ConfigureAwait(false);
        return ToStatus(settings.ContributionCollectorEnabled, checkpoint);
    }

    public async Task MarkQueuedAsync(CancellationToken token = default)
    {
        ContributionCheckpointV1 checkpoint = await _checkpoints.ReadAsync(token).ConfigureAwait(false);
        if (checkpoint.State is not ContributionStates.Running and not ContributionStates.Yielding)
        {
            checkpoint.State = ContributionStates.Queued;
            checkpoint.LastError = null;
            await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
        }
    }

    public async Task<int> RunAsync(CancellationToken token = default)
    {
        await RunGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EditableSettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            ContributionCheckpointV1 checkpoint = await _checkpoints.ReadAsync(token).ConfigureAwait(false);
            if (!settings.ContributionCollectorEnabled)
            {
                checkpoint.State = ContributionStates.Disabled;
                checkpoint.LastError = null;
                await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
                return 0;
            }

            // A run is resumed when the previous one was interrupted mid-flight: either the
            // process died while running/yielding, or a cancellation re-queued it while it
            // still had completed assignments recorded.
            bool resuming = checkpoint.State is ContributionStates.Running or ContributionStates.Yielding
                || (checkpoint.State == ContributionStates.Queued && checkpoint.CompletedAssignments.Count > 0);
            if (!resuming)
            {
                checkpoint.CompletedAssignments.Clear();
                checkpoint.LastStartedUtc = DateTime.UtcNow;
                checkpoint.ItemsCollected = 0;
            }
            checkpoint.State = ContributionStates.Running;
            checkpoint.LastError = null;
            await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);

            if (settings.ContributionPackageAllowlist.Length == 0 || settings.ContributionSourceAllowlist.Length == 0)
            {
                await CompleteAsync(checkpoint, token).ConfigureAwait(false);
                return 0;
            }

            List<ContributionAssignment> assignments = BuildAssignments(settings);
            if (assignments.Count == 0)
            {
                await CompleteAsync(checkpoint, token).ConfigureAwait(false);
                return 0;
            }
            Preferences preferences = await _mihon.GetPreferencesAsync(token).ConfigureAwait(false);
            int collected = checkpoint.ItemsCollected;
            foreach (IGrouping<string, ContributionAssignment> packageAssignments in
                     assignments.GroupBy(a => a.Extension.Package, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                List<ContributionAssignment> pending = packageAssignments
                    .Where(a => !checkpoint.CompletedAssignments.Contains(a.Key)).ToList();
                if (pending.Count == 0)
                    continue;
                DiscoveryArtifact artifact = await _mihon.PrepareDiscoveryArtifactsAsync(pending[0].Extension, token)
                    .ConfigureAwait(false);
                var prepared = new DiscoveryWorkerExtension { Entry = artifact.Entry, Folder = artifact.Folder };
                while (true)
                {
                    if (_interactive.IsActive)
                    {
                        checkpoint.State = ContributionStates.Yielding;
                        await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
                        await _interactive.WaitUntilIdleAsync(token).ConfigureAwait(false);
                        checkpoint.State = ContributionStates.Running;
                        await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
                    }

                    ContributionWorkerOutcome outcome = await _workers.RunAsync(
                        prepared, pending.Select(a => a.SourceId).ToArray(), preferences, token).ConfigureAwait(false);
                    if (outcome.Kind == ContributionWorkerOutcomeKind.Yielded)
                        continue;
                    int written = await _sink.WriteAsync(outcome.Batch ?? new ContributionBatchV1(), token).ConfigureAwait(false);
                    collected += written;
                    checkpoint.ItemsCollected = collected;
                    foreach (ContributionAssignment assignment in pending)
                        checkpoint.CompletedAssignments.Add(assignment.Key);
                    await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
                    break;
                }
            }

            await CompleteAsync(checkpoint, token).ConfigureAwait(false);
            return collected;
        }
        catch (OperationCanceledException)
        {
            // Cancellation (shutdown, job abort) must not leave the checkpoint stuck in a
            // "running"/"yielding" state: record it as queued so the completed assignments stay
            // resumable and the status no longer reports an active run.
            ContributionCheckpointV1 checkpoint = await _checkpoints.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (checkpoint.State is ContributionStates.Running or ContributionStates.Yielding)
            {
                checkpoint.State = ContributionStates.Queued;
                await _checkpoints.WriteAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            ContributionCheckpointV1 checkpoint = await _checkpoints.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            checkpoint.State = ContributionStates.Failed;
            checkpoint.LastError = ex.Message;
            await _checkpoints.WriteAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
            _logger.LogError(ex, "Contribution collection failed.");
            throw;
        }
        finally
        {
            RunGate.Release();
        }
    }

    private List<ContributionAssignment> BuildAssignments(EditableSettingsDto settings)
    {
        var allowedPackages = settings.ContributionPackageAllowlist
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedSources = settings.ContributionSourceAllowlist
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<ContributionAssignment>();
        foreach (TachiyomiExtension extension in _mihon.ListOnlineRepositories()
                     .SelectMany(r => r.Extensions)
                     .Where(e => allowedPackages.Contains(e.Package))
                     .OrderBy(e => e.Package, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenPackages.Add(extension.Package))
                continue;
            foreach (TachiyomiSource source in extension.Sources)
            {
                string key = extension.Package + "|" + source.Id;
                if (!allowedSources.Contains(key))
                    continue;
                if (!long.TryParse(source.Id, out long sourceId))
                {
                    _logger.LogWarning("Contribution source allowlist entry {Source} has a non-numeric source id; skipping.", key);
                    continue;
                }
                assignments.Add(new ContributionAssignment(key, extension, sourceId));
            }
        }
        return assignments.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task CompleteAsync(ContributionCheckpointV1 checkpoint, CancellationToken token)
    {
        checkpoint.State = ContributionStates.Completed;
        checkpoint.LastCompletedUtc = DateTime.UtcNow;
        checkpoint.LastError = null;
        checkpoint.CompletedAssignments.Clear();
        await _checkpoints.WriteAsync(checkpoint, token).ConfigureAwait(false);
    }

    private static ContributionStatusDto ToStatus(bool enabled, ContributionCheckpointV1 checkpoint)
    {
        string state = enabled
            ? checkpoint.State == ContributionStates.Disabled ? ContributionStates.Idle : checkpoint.State
            : ContributionStates.Disabled;
        return new ContributionStatusDto
        {
            Enabled = enabled,
            State = state,
            LastStartedUtc = checkpoint.LastStartedUtc,
            LastCompletedUtc = checkpoint.LastCompletedUtc,
            ItemsCollected = checkpoint.ItemsCollected,
            LastError = checkpoint.LastError
        };
    }

    private sealed record ContributionAssignment(string Key, TachiyomiExtension Extension, long SourceId);
}
