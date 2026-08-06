using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Settings;
using System.Security.Cryptography;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Snapshot;

/// <summary>
/// Standalone retryable job that downloads the public contribution snapshot export
/// (titles.json, sources.json, metadata.json), decrypts each source blob with the worker's
/// public key, and writes a decoded snapshot-v1.json to disk. Conditional requests (ETags in
/// snapshot-state-v1.json) let an unchanged export short-circuit without decoding.
///
/// This job requires no credentials: it must never read or log the contributor UUID. The
/// snapshot files and the /key endpoint are public, so nothing here is secret.
/// </summary>
public sealed class ContributionSnapshotDownloader
{
    private const string TitlesFile = "titles.json";
    private const string SourcesFile = "sources.json";
    private const string MetadataFile = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly TimeSpan[] DefaultBackoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)];

    private readonly SettingsService _settings;
    private readonly ContributionSnapshotClient _client;
    private readonly IContributionSnapshotStateStore _states;
    private readonly ILogger<ContributionSnapshotDownloader> _logger;
    private readonly string _snapshotFilePath;
    private readonly TimeSpan[] _backoff;

    public ContributionSnapshotDownloader(
        SettingsService settings,
        ContributionSnapshotClient client,
        IContributionSnapshotStateStore states,
        IConfiguration configuration,
        ILogger<ContributionSnapshotDownloader> logger)
        : this(settings, client, states,
            Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "snapshot", "snapshot-v1.json"),
            logger)
    {
    }

    public ContributionSnapshotDownloader(
        SettingsService settings,
        ContributionSnapshotClient client,
        IContributionSnapshotStateStore states,
        string snapshotFilePath,
        ILogger<ContributionSnapshotDownloader> logger,
        TimeSpan[]? backoff = null)
    {
        _settings = settings;
        _client = client;
        _states = states;
        _snapshotFilePath = snapshotFilePath;
        _logger = logger;
        _backoff = backoff is { Length: > 0 } ? backoff : DefaultBackoff;
    }

    public async Task<ContributionSnapshotStatusDto> GetStatusAsync(CancellationToken token = default)
    {
        SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
        ContributionSnapshotStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
        return ToStatus(settings.ContributionSnapshotEnabled, state);
    }

    public async Task MarkQueuedAsync(CancellationToken token = default)
    {
        ContributionSnapshotStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
        if (state.State != ContributionSnapshotStates.Running)
        {
            state.State = ContributionSnapshotStates.Queued;
            state.LastError = null;
            await _states.WriteAsync(state, token).ConfigureAwait(false);
        }
    }

    public async Task RunAsync(CancellationToken token = default)
    {
        await RunGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SettingsDto settings = await _settings.GetSettingsAsync(token).ConfigureAwait(false);
            ContributionSnapshotStateV1 state = await _states.ReadAsync(token).ConfigureAwait(false);
            if (!settings.ContributionSnapshotEnabled)
            {
                state.State = ContributionSnapshotStates.Disabled;
                state.LastError = null;
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return;
            }

            state.State = ContributionSnapshotStates.Running;
            state.LastStartedUtc = DateTime.UtcNow;
            state.LastError = null;
            await _states.WriteAsync(state, token).ConfigureAwait(false);

            // A different export shares no conditional-request state: a URL change invalidates
            // every stored ETag so the next fetch re-downloads and re-decodes from scratch.
            string snapshotUrl = (settings.ContributionSnapshotUrl ?? string.Empty).TrimEnd('/');
            if (!string.Equals(state.SnapshotUrl, snapshotUrl, StringComparison.OrdinalIgnoreCase))
            {
                state.ETags.Clear();
                state.SnapshotUrl = snapshotUrl;
            }

            // Conditional fetch of the trio; a retryable failure on any file aborts the run.
            SnapshotFileResult titles = await FetchWithBackoffAsync(snapshotUrl, TitlesFile,
                state.ETags.GetValueOrDefault(TitlesFile), token).ConfigureAwait(false);
            SnapshotFileResult sources = await FetchWithBackoffAsync(snapshotUrl, SourcesFile,
                state.ETags.GetValueOrDefault(SourcesFile), token).ConfigureAwait(false);
            SnapshotFileResult metadata = await FetchWithBackoffAsync(snapshotUrl, MetadataFile,
                state.ETags.GetValueOrDefault(MetadataFile), token).ConfigureAwait(false);

            string? transportError = FirstTransportError(titles, sources, metadata);
            if (transportError is not null)
            {
                await FailAsync(state, transportError, token).ConfigureAwait(false);
                return;
            }

            // Nothing changed and the last decoded snapshot is still on disk: keep the previous
            // counters and skip the key fetch and decode entirely.
            if (titles.Status == SnapshotFetchStatus.NotModified &&
                sources.Status == SnapshotFetchStatus.NotModified &&
                metadata.Status == SnapshotFetchStatus.NotModified &&
                File.Exists(_snapshotFilePath))
            {
                state.LastRunUnchanged = true;
                state.State = ContributionSnapshotStates.Completed;
                state.LastCompletedUtc = DateTime.UtcNow;
                state.LastError = null;
                await _states.WriteAsync(state, token).ConfigureAwait(false);
                return;
            }

            // Re-fetch any 304'd file unconditionally so the decoded trio is internally consistent.
            ResolvedFile titlesFile = await ResolveAsync(snapshotUrl, TitlesFile, titles, token).ConfigureAwait(false);
            ResolvedFile sourcesFile = await ResolveAsync(snapshotUrl, SourcesFile, sources, token).ConfigureAwait(false);
            ResolvedFile metadataFile = await ResolveAsync(snapshotUrl, MetadataFile, metadata, token).ConfigureAwait(false);

            transportError = FirstTransportError(titlesFile.Result, sourcesFile.Result, metadataFile.Result);
            if (transportError is not null)
            {
                await FailAsync(state, transportError, token).ConfigureAwait(false);
                return;
            }

            // The decryption key lives on the worker's public /key endpoint, not the snapshot URL.
            SnapshotKeyResult key = await _client
                .GetKeyAsync(settings.ContributionUploadUrl, token).ConfigureAwait(false);
            if (!key.Success || key.Key is null || key.Iv is null)
            {
                await FailAsync(state, key.Error ?? "The decryption key could not be fetched.", token).ConfigureAwait(false);
                return;
            }

            // titles.json / sources.json are required; a 404 or malformed body fails the run and
            // leaves any previously written snapshot untouched.
            if (titlesFile.Result.Status == SnapshotFetchStatus.NotFound)
            {
                await FailAsync(state, "titles.json was not found at the snapshot URL.", token).ConfigureAwait(false);
                return;
            }
            if (sourcesFile.Result.Status == SnapshotFetchStatus.NotFound)
            {
                await FailAsync(state, "sources.json was not found at the snapshot URL.", token).ConfigureAwait(false);
                return;
            }

            List<SnapshotTitleRow>? titleRows = TryParse<List<SnapshotTitleRow>>(titlesFile.Result.Body);
            if (titleRows is null)
            {
                await FailAsync(state, "titles.json could not be parsed.", token).ConfigureAwait(false);
                return;
            }
            List<SnapshotSourceRow>? sourceRows = TryParse<List<SnapshotSourceRow>>(sourcesFile.Result.Body);
            if (sourceRows is null)
            {
                await FailAsync(state, "sources.json could not be parsed.", token).ConfigureAwait(false);
                return;
            }

            // metadata.json is best-effort: a 404 or malformed body degrades to an empty link set.
            List<SnapshotMetadataRow> metadataRows;
            if (metadataFile.Result.Status == SnapshotFetchStatus.NotFound)
            {
                _logger.LogWarning("Contribution snapshot metadata.json not found; continuing with no metadata links.");
                metadataRows = [];
            }
            else
            {
                List<SnapshotMetadataRow>? parsed = TryParse<List<SnapshotMetadataRow>>(metadataFile.Result.Body);
                if (parsed is null)
                {
                    _logger.LogWarning("Contribution snapshot metadata.json could not be parsed; continuing with no metadata links.");
                    metadataRows = [];
                }
                else
                {
                    metadataRows = parsed;
                }
            }

            // Duplicate title ids are last-wins; duplicate title strings are fine and kept.
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            bool duplicateTitleId = false;
            foreach (SnapshotTitleRow row in titleRows)
            {
                if (lookup.ContainsKey(row.Id))
                    duplicateTitleId = true;
                lookup[row.Id] = row.Title;
            }
            if (duplicateTitleId)
                _logger.LogWarning("Contribution snapshot titles.json contains duplicate ids; last occurrence wins.");

            int recordsDecoded = 0, recordsNullData = 0, recordsFailed = 0, danglingTitleRefs = 0, nonNullRows = 0;
            var records = new List<ContributionSnapshotRecordV1>();
            foreach (SnapshotSourceRow row in sourceRows)
            {
                token.ThrowIfCancellationRequested();
                if (row.Data is null)
                {
                    recordsNullData++;
                    continue;
                }
                nonNullRows++;

                byte[] cipher;
                try
                {
                    cipher = Convert.FromBase64String(row.Data);
                }
                catch (FormatException)
                {
                    recordsFailed++;
                    _logger.LogWarning("Contribution snapshot record {Key} has non-base64 data; skipping.", row.Id);
                    continue;
                }

                ContributionBlobPayloadV1 payload;
                try
                {
                    byte[] envelope = ContributionSnapshotCrypto.Decrypt(key.Key, key.Iv, cipher);
                    payload = ContributionBlobEnvelope.Decode(envelope);
                }
                catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException)
                {
                    recordsFailed++;
                    _logger.LogWarning("Contribution snapshot record {Key} failed to decode: {Message}", row.Id, ex.Message);
                    continue;
                }

                bool dangling = !lookup.ContainsKey(row.TitleId);
                if (dangling)
                    danglingTitleRefs++;
                records.Add(new ContributionSnapshotRecordV1
                {
                    Key = row.Id,
                    TitleId = row.TitleId,
                    TitleIdDangling = dangling,
                    Payload = payload
                });
                recordsDecoded++;
            }

            // Every non-null row failing means the key or export is mismatched; refuse to overwrite
            // a good previous snapshot with an empty one.
            if (recordsDecoded == 0 && nonNullRows > 0)
            {
                await FailAsync(state, "all records failed to decode — key/export mismatch?", token).ConfigureAwait(false);
                return;
            }

            var snapshot = new ContributionSnapshotV1
            {
                GeneratedUtc = DateTime.UtcNow,
                Titles = titleRows.Select(t => new ContributionSnapshotTitleV1 { Id = t.Id, Title = t.Title }).ToList(),
                Records = records,
                Metadata = metadataRows.Select(m => new ContributionSnapshotMetadataV1
                {
                    TitleId = m.TitleId,
                    Provider = m.Provider,
                    ProviderKey = m.ProviderKey,
                    Type = m.Type
                }).ToList()
            };
            await WriteSnapshotAsync(snapshot, token).ConfigureAwait(false);

            ApplyETag(state, TitlesFile, titlesFile.ETag);
            ApplyETag(state, SourcesFile, sourcesFile.ETag);
            ApplyETag(state, MetadataFile, metadataFile.ETag, remove: metadataFile.Result.Status == SnapshotFetchStatus.NotFound);

            state.LastRunUnchanged = false;
            state.Titles = titleRows.Count;
            state.RecordsDecoded = recordsDecoded;
            state.RecordsNullData = recordsNullData;
            state.RecordsFailed = recordsFailed;
            state.DanglingTitleRefs = danglingTitleRefs;
            state.MetadataLinks = metadataRows.Count;
            state.State = ContributionSnapshotStates.Completed;
            state.LastCompletedUtc = DateTime.UtcNow;
            state.LastError = null;
            await _states.WriteAsync(state, token).ConfigureAwait(false);
            _logger.LogInformation(
                "Contribution snapshot download finished: {Titles} titles, {Decoded} decoded, {Skipped} null, {Failed} failed, {Dangling} dangling.",
                titleRows.Count, recordsDecoded, recordsNullData, recordsFailed, danglingTitleRefs);
        }
        catch (OperationCanceledException)
        {
            ContributionSnapshotStateV1 state = await _states.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (state.State == ContributionSnapshotStates.Running)
            {
                state.State = ContributionSnapshotStates.Queued;
                await _states.WriteAsync(state, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception ex)
        {
            ContributionSnapshotStateV1 state = await _states.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            state.State = ContributionSnapshotStates.Failed;
            state.LastError = ex.Message;
            await _states.WriteAsync(state, CancellationToken.None).ConfigureAwait(false);
            _logger.LogError(ex, "Contribution snapshot download failed.");
            throw;
        }
        finally
        {
            RunGate.Release();
        }
    }

    private async Task<SnapshotFileResult> FetchWithBackoffAsync(string baseUrl, string fileName, string? etag,
        CancellationToken token)
    {
        SnapshotFileResult result = new() { Status = SnapshotFetchStatus.RetryableError };
        for (int attempt = 0; attempt < _backoff.Length; attempt++)
        {
            result = await _client.GetFileAsync(baseUrl, fileName, etag, token).ConfigureAwait(false);
            if (result.Status != SnapshotFetchStatus.RetryableError)
                return result;
            if (attempt < _backoff.Length - 1)
                await Task.Delay(_backoff[attempt], token).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>
    /// Turns a conditional fetch into a resolved body + ETag: a 304 is re-fetched unconditionally
    /// so the decoded trio comes from one consistent export generation.
    /// </summary>
    private async Task<ResolvedFile> ResolveAsync(string baseUrl, string fileName, SnapshotFileResult conditional,
        CancellationToken token)
    {
        if (conditional.Status == SnapshotFetchStatus.NotModified)
        {
            SnapshotFileResult refetched = await FetchWithBackoffAsync(baseUrl, fileName, null, token).ConfigureAwait(false);
            return new ResolvedFile(refetched, refetched.ETag);
        }
        return new ResolvedFile(conditional, conditional.ETag);
    }

    private async Task FailAsync(ContributionSnapshotStateV1 state, string error, CancellationToken token)
    {
        state.State = ContributionSnapshotStates.Failed;
        state.LastError = error;
        await _states.WriteAsync(state, token).ConfigureAwait(false);
        _logger.LogWarning("Contribution snapshot download failed: {Error}", error);
    }

    private static string? FirstTransportError(params SnapshotFileResult[] results)
        => results.FirstOrDefault(r => r.Status == SnapshotFetchStatus.RetryableError)?.Error
           ?? (results.Any(r => r.Status == SnapshotFetchStatus.RetryableError)
               ? "The snapshot export could not be reached."
               : null);

    private static void ApplyETag(ContributionSnapshotStateV1 state, string fileName, string? etag, bool remove = false)
    {
        if (remove)
            state.ETags.Remove(fileName);
        else if (!string.IsNullOrEmpty(etag))
            state.ETags[fileName] = etag;
    }

    private static T? TryParse<T>(byte[]? body) where T : class
    {
        if (body is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteSnapshotAsync(ContributionSnapshotV1 snapshot, CancellationToken token)
    {
        string? directory = Path.GetDirectoryName(_snapshotFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string tempPath = _snapshotFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(tempPath, _snapshotFilePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static ContributionSnapshotStatusDto ToStatus(bool enabled, ContributionSnapshotStateV1 state)
    {
        string reported = enabled
            ? state.State == ContributionSnapshotStates.Disabled ? ContributionSnapshotStates.Idle : state.State
            : ContributionSnapshotStates.Disabled;
        return new ContributionSnapshotStatusDto
        {
            Enabled = enabled,
            State = reported,
            LastStartedUtc = state.LastStartedUtc,
            LastCompletedUtc = state.LastCompletedUtc,
            Unchanged = state.LastRunUnchanged,
            Titles = state.Titles,
            RecordsDecoded = state.RecordsDecoded,
            RecordsSkipped = state.RecordsNullData,
            RecordsFailed = state.RecordsFailed,
            DanglingTitleRefs = state.DanglingTitleRefs,
            MetadataLinks = state.MetadataLinks,
            LastError = state.LastError
        };
    }

    private readonly record struct ResolvedFile(SnapshotFileResult Result, string? ETag);
}
