using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Settings;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Upload;

/// <summary>
/// Standalone retryable job that reads the local collector's contributions-v1.json and
/// uploads new/changed records to the contribution worker. Delta state (what the server
/// already confirmed) lives in upload-state-v1.json so unchanged records are never
/// re-uploaded (the worker's <c>add</c> rewrites rows unconditionally, so skipping
/// client-side is the only way to avoid rewriting every row on every run).
/// </summary>
public sealed class ContributionUploader
{
    public const int BatchSize = 50;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly TimeSpan[] DefaultBackoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)];

    private readonly SettingsService _settings;
    private readonly ContributionUploadClient _client;
    private readonly IContributionUploadStateStore _states;
    private readonly ILogger<ContributionUploader> _logger;
    private readonly string _contributionsPath;
    private readonly TimeSpan[] _backoff;

    public ContributionUploader(
        SettingsService settings,
        ContributionUploadClient client,
        IContributionUploadStateStore states,
        IConfiguration configuration,
        ILogger<ContributionUploader> logger)
        : this(settings, client, states,
            Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "contributions-v1.json"),
            logger)
    {
    }

    public ContributionUploader(
        SettingsService settings,
        ContributionUploadClient client,
        IContributionUploadStateStore states,
        string contributionsPath,
        ILogger<ContributionUploader> logger,
        TimeSpan[]? backoff = null)
    {
        _settings = settings;
        _client = client;
        _states = states;
        _contributionsPath = contributionsPath;
        _logger = logger;
        _backoff = backoff is { Length: > 0 } ? backoff : DefaultBackoff;
    }

    public async Task<ContributionUploadStatusDto> GetStatusAsync(CancellationToken token = default)
    {
        SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        ContributionUploadStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
        return ToStatus(settings.ContributionUploadEnabled, state);
    }

    public async Task MarkQueuedAsync(CancellationToken token = default)
    {
        ContributionUploadStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
        if (state.State != ContributionUploadStates.Running)
        {
            state.State = ContributionUploadStates.Queued;
            state.LastError = null;
            await _states.WriteAsync(state, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Live-validates the configured contributor UUID against the worker and persists the
    /// outcome so the status surface can show it without another network call.
    /// </summary>
    public async Task<ContributionContributorDto> ValidateContributorAsync(CancellationToken token = default)
    {
        SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        if (!Guid.TryParse(settings.ContributionContributorUuid, out _))
            return new ContributionContributorDto { Valid = false, Error = "No contributor UUID is configured." };

        ContributorProbeResult probe = await _client
            .GetContributorAsync(settings.ContributionUploadUrl, settings.ContributionContributorUuid, token)
            .ConfigureAwait(false);
        if (probe.Status == ContributionCallStatus.RetryableError)
            return new ContributionContributorDto { Valid = false, Error = probe.Error ?? "The contribution worker could not be reached." };

        var snapshot = new ContributionContributorSnapshotV1
        {
            Valid = probe.Status == ContributionCallStatus.Success,
            Active = probe.Contributor?.Active ?? false,
            Admin = probe.Contributor?.Admin ?? false,
            BanReason = probe.Contributor?.BanReason ?? (probe.Status == ContributionCallStatus.Banned ? probe.Error : null),
            ValidatedUtc = DateTime.UtcNow
        };
        ContributionUploadStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
        state.Contributor = snapshot;
        await _states.WriteAsync(state, token).ConfigureAwait(false);
        return ToContributorDto(snapshot);
    }

    public async Task<int> RunAsync(CancellationToken token = default)
    {
        await RunGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            ContributionUploadStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
            if (!settings.ContributionUploadEnabled)
            {
                state.State = ContributionUploadStates.Disabled;
                state.LastError = null;
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return 0;
            }

            state.LastStartedUtc = DateTime.UtcNow;
            state.Uploaded = 0;
            state.Skipped = 0;
            state.Failed = 0;
            state.LastError = null;

            // A different worker knows nothing about past uploads: the delta store only
            // has meaning against the URL it was built for. A UUID change keeps it (same
            // server-side content, just a different owner on subsequent adds).
            string serverUrl = (settings.ContributionUploadUrl ?? string.Empty).TrimEnd('/');
            if (!string.Equals(state.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase))
            {
                state.Entries.Clear();
                state.ServerUrl = serverUrl;
            }

            if (!Guid.TryParse(settings.ContributionContributorUuid, out _))
            {
                state.State = ContributionUploadStates.Invalid;
                state.LastError = "No valid contributor UUID is configured.";
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return 0;
            }

            state.State = ContributionUploadStates.Running;
            await _states.WriteAsync(state, token).ConfigureAwait(false);

            ContributorProbeResult probe = await _client
                .GetContributorAsync(serverUrl, settings.ContributionContributorUuid, token).ConfigureAwait(false);
            if (!await ApplyProbeAsync(state, probe, token).ConfigureAwait(false))
                return 0;

            ContributionBatchV1? contributions = await ReadContributionsAsync(token).ConfigureAwait(false);
            if (contributions is null || contributions.Records.Count == 0)
            {
                await CompleteAsync(state, token).ConfigureAwait(false);
                return 0;
            }

            List<PendingUpload> pending = BuildPending(contributions, state);
            foreach (PendingUpload[] chunk in pending.Chunk(BatchSize))
            {
                token.ThrowIfCancellationRequested();
                UploadCallResult result = await UploadWithBackoffAsync(state, serverUrl,
                    settings.ContributionContributorUuid, chunk, token).ConfigureAwait(false);
                if (result.Status != ContributionCallStatus.Success)
                    return state.Uploaded;

                var errorIndices = new Dictionary<int, string>();
                foreach (UploadItemError error in result.Response?.Errors ?? [])
                    errorIndices[error.Index] = error.Message;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (errorIndices.TryGetValue(i, out string? message))
                    {
                        state.Failed++;
                        _logger.LogWarning("Contribution item {Key} rejected by worker: {Message}",
                            chunk[i].Key, message);
                    }
                    else
                    {
                        state.Entries[chunk[i].Key] = new ContributionUploadEntryV1
                        {
                            Hash = chunk[i].Hash,
                            UploadedUtc = DateTime.UtcNow
                        };
                        state.Uploaded++;
                    }
                }
                await _states.WriteAsync(state, token).ConfigureAwait(false);
            }

            await CompleteAsync(state, token).ConfigureAwait(false);
            _logger.LogInformation(
                "Contribution upload finished: {Uploaded} uploaded, {Skipped} unchanged, {Failed} rejected.",
                state.Uploaded, state.Skipped, state.Failed);
            return state.Uploaded;
        }
        catch (OperationCanceledException)
        {
            ContributionUploadStateV1 state = await _states.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (state.State == ContributionUploadStates.Running)
            {
                state.State = ContributionUploadStates.Queued;
                await _states.WriteAsync(state, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            ContributionUploadStateV1 state = await _states.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            state.State = ContributionUploadStates.Failed;
            state.LastError = ex.Message;
            await _states.WriteAsync(state, CancellationToken.None).ConfigureAwait(false);
            _logger.LogError(ex, "Contribution upload failed.");
            throw;
        }
        finally
        {
            RunGate.Release();
        }
    }

    /// <summary>Returns false when the probe outcome aborts the run (state already persisted).</summary>
    private async Task<bool> ApplyProbeAsync(ContributionUploadStateV1 state, ContributorProbeResult probe,
        CancellationToken token)
    {
        switch (probe.Status)
        {
            case ContributionCallStatus.UnknownContributor:
                state.State = ContributionUploadStates.Invalid;
                state.LastError = "The contribution worker does not recognize the configured contributor UUID.";
                state.Contributor = new ContributionContributorSnapshotV1 { Valid = false, ValidatedUtc = DateTime.UtcNow };
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return false;
            case ContributionCallStatus.Banned:
                Ban(state, probe.Error);
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return false;
            case ContributionCallStatus.RetryableError:
                state.State = ContributionUploadStates.Failed;
                state.LastError = probe.Error ?? "The contribution worker could not be reached.";
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return false;
        }

        state.Contributor = new ContributionContributorSnapshotV1
        {
            Valid = true,
            Active = probe.Contributor?.Active ?? false,
            Admin = probe.Contributor?.Admin ?? false,
            BanReason = probe.Contributor?.BanReason,
            ValidatedUtc = DateTime.UtcNow
        };
        if (probe.Contributor is { Active: false })
        {
            state.State = ContributionUploadStates.Banned;
            state.LastError = string.IsNullOrWhiteSpace(probe.Contributor.BanReason)
                ? "The contributor is inactive."
                : $"The contributor is banned: {probe.Contributor.BanReason}";
            await _states.WriteAsync(state, token).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private async Task<UploadCallResult> UploadWithBackoffAsync(ContributionUploadStateV1 state, string serverUrl,
        string contributorUuid, PendingUpload[] chunk, CancellationToken token)
    {
        UploadItem[] items = chunk.Select(p => p.Item).ToArray();
        UploadCallResult result = new() { Status = ContributionCallStatus.RetryableError };
        for (int attempt = 0; attempt < _backoff.Length; attempt++)
        {
            result = await _client.UploadAsync(serverUrl, contributorUuid, items, token).ConfigureAwait(false);
            switch (result.Status)
            {
                case ContributionCallStatus.Success:
                    return result;
                case ContributionCallStatus.Banned:
                    Ban(state, result.Error);
                    await _states.WriteAsync(state, token).ConfigureAwait(false);
                    return result;
                case ContributionCallStatus.UnknownContributor:
                    state.State = ContributionUploadStates.Invalid;
                    state.LastError = "The contribution worker does not recognize the configured contributor UUID.";
                    await _states.WriteAsync(state, token).ConfigureAwait(false);
                    return result;
            }
            if (attempt < _backoff.Length - 1)
                await Task.Delay(_backoff[attempt], token).ConfigureAwait(false);
        }
        state.State = ContributionUploadStates.Failed;
        state.LastError = result.Error ?? "The contribution worker kept failing the batch.";
        await _states.WriteAsync(state, token).ConfigureAwait(false);
        return result;
    }

    private static void Ban(ContributionUploadStateV1 state, string? reason)
    {
        state.State = ContributionUploadStates.Banned;
        state.LastError = string.IsNullOrWhiteSpace(reason)
            ? "The contributor is banned."
            : $"The contributor is banned: {reason}";
        state.Contributor = new ContributionContributorSnapshotV1
        {
            Valid = true,
            Active = false,
            BanReason = reason,
            ValidatedUtc = DateTime.UtcNow
        };
    }

    private List<PendingUpload> BuildPending(ContributionBatchV1 contributions, ContributionUploadStateV1 state)
    {
        // In-run dedupe is last-wins on the wire identity: distinct internal records can
        // collapse to the same MD5 key, and uploading duplicates in one batch is a
        // worker-side PK violation that rolls back the whole batch.
        var deduped = new Dictionary<string, (ContributionRecordV1 Record, ContributionBlobPayloadV1 Payload, string Hash)>(StringComparer.Ordinal);
        foreach (ContributionRecordV1 record in contributions.Records)
        {
            if (string.IsNullOrWhiteSpace(record.Package) || string.IsNullOrWhiteSpace(record.Url))
                continue;
            string key = ContributionUploadKey.Create(record);
            ContributionBlobPayloadV1 payload = ContributionBlobPayloadV1.FromRecord(record, contributions.GeneratedUtc);
            deduped[key] = (record, payload, ContributionBlobEnvelope.PayloadHash(payload));
        }

        var pending = new List<PendingUpload>();
        foreach ((string key, (ContributionRecordV1 record, ContributionBlobPayloadV1 payload, string hash)) in deduped)
        {
            if (state.Entries.TryGetValue(key, out ContributionUploadEntryV1? entry) &&
                string.Equals(entry.Hash, hash, StringComparison.Ordinal))
            {
                state.Skipped++;
                continue;
            }
            pending.Add(new PendingUpload(key, hash, new UploadItem
            {
                Type = UploadItemTypes.Source,
                Action = UploadItemActions.Add,
                Data = new SourceItemData
                {
                    Id = key,
                    Title = record.Title,
                    Data = ContributionBlobEnvelope.EncodeBase64(payload)
                }
            }));
        }
        return pending.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
    }

    private async Task<ContributionBatchV1?> ReadContributionsAsync(CancellationToken token)
    {
        if (!File.Exists(_contributionsPath))
            return null;
        await using FileStream stream = File.OpenRead(_contributionsPath);
        return await JsonSerializer.DeserializeAsync<ContributionBatchV1>(stream, JsonOptions, token).ConfigureAwait(false);
    }

    private async Task CompleteAsync(ContributionUploadStateV1 state, CancellationToken token)
    {
        state.State = ContributionUploadStates.Completed;
        state.LastCompletedUtc = DateTime.UtcNow;
        state.LastError = null;
        await _states.WriteAsync(state, token).ConfigureAwait(false);
    }

    private static ContributionUploadStatusDto ToStatus(bool enabled, ContributionUploadStateV1 state)
    {
        string reported = enabled
            ? state.State == ContributionUploadStates.Disabled ? ContributionUploadStates.Idle : state.State
            : ContributionUploadStates.Disabled;
        return new ContributionUploadStatusDto
        {
            Enabled = enabled,
            State = reported,
            LastStartedUtc = state.LastStartedUtc,
            LastCompletedUtc = state.LastCompletedUtc,
            Uploaded = state.Uploaded,
            Skipped = state.Skipped,
            Failed = state.Failed,
            LastError = state.LastError,
            Contributor = state.Contributor is null ? null : ToContributorDto(state.Contributor)
        };
    }

    private static ContributionContributorDto ToContributorDto(ContributionContributorSnapshotV1 snapshot) => new()
    {
        Valid = snapshot.Valid,
        Active = snapshot.Active,
        Admin = snapshot.Admin,
        BanReason = snapshot.BanReason,
        ValidatedUtc = snapshot.ValidatedUtc
    };

    private sealed record PendingUpload(string Key, string Hash, UploadItem Item);
}
