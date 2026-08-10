using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Snapshot;

public sealed class ContributionSnapshotStateV1
{
    public int Version { get; set; } = 1;
    public string State { get; set; } = ContributionSnapshotStates.Idle;
    /// <summary>
    /// The snapshot base URL the <see cref="ETags"/> were captured against. When the configured
    /// URL changes the ETags are cleared (a different export shares no conditional-request state).
    /// </summary>
    public string? SnapshotUrl { get; set; }
    /// <summary>Per-file ETag from the last successful fetch, keyed by file name.</summary>
    public Dictionary<string, string> ETags { get; set; } = new(StringComparer.Ordinal);
    public DateTime? LastStartedUtc { get; set; }
    public DateTime? LastCompletedUtc { get; set; }
    public bool LastRunUnchanged { get; set; }
    public int Titles { get; set; }
    public int RecordsDecoded { get; set; }
    public int RecordsNullData { get; set; }
    public int RecordsFailed { get; set; }
    public int DanglingTitleRefs { get; set; }
    public int MetadataLinks { get; set; }
    public string? LastError { get; set; }
}

public interface IContributionSnapshotStateStore
{
    Task<ContributionSnapshotStateV1> ReadAsync(CancellationToken token = default);
    Task WriteAsync(ContributionSnapshotStateV1 state, CancellationToken token = default);
}

public sealed class JsonContributionSnapshotStateStore : IContributionSnapshotStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonContributionSnapshotStateStore(IConfiguration configuration)
        : this(Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "snapshot", "snapshot-state-v1.json"))
    {
    }

    public JsonContributionSnapshotStateStore(string path)
    {
        _path = path;
    }

    public async Task<ContributionSnapshotStateV1> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new ContributionSnapshotStateV1();
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ContributionSnapshotStateV1>(stream, JsonOptions, token).ConfigureAwait(false)
                   ?? new ContributionSnapshotStateV1();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ContributionSnapshotStateV1 state, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, state, JsonOptions, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }
                File.Move(tempPath, _path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
