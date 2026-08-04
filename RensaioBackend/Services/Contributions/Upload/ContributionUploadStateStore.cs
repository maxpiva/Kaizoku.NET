using System.Text.Json;

namespace RensaioBackend.Services.Contributions.Upload;

public sealed class ContributionUploadStateV1
{
    public int Version { get; set; } = 1;
    public string State { get; set; } = ContributionUploadStates.Idle;
    /// <summary>
    /// The worker base URL the <see cref="Entries"/> delta store was built against. When the
    /// configured URL changes the entries are cleared (a different server knows nothing about
    /// past uploads); a contributor UUID change keeps them (same server, same content).
    /// </summary>
    public string? ServerUrl { get; set; }
    public ContributionContributorSnapshotV1? Contributor { get; set; }
    public DateTime? LastStartedUtc { get; set; }
    public DateTime? LastCompletedUtc { get; set; }
    public int Uploaded { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public string? LastError { get; set; }
    /// <summary>Delta store: MD5 upload key → content hash confirmed uploaded.</summary>
    public Dictionary<string, ContributionUploadEntryV1> Entries { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ContributionUploadEntryV1
{
    public string Hash { get; set; } = string.Empty;
    public DateTime UploadedUtc { get; set; }
}

public sealed class ContributionContributorSnapshotV1
{
    public bool Valid { get; set; }
    public bool Active { get; set; }
    public bool Admin { get; set; }
    public string? BanReason { get; set; }
    public DateTime? ValidatedUtc { get; set; }
}

public interface IContributionUploadStateStore
{
    Task<ContributionUploadStateV1> ReadAsync(CancellationToken token = default);
    Task WriteAsync(ContributionUploadStateV1 state, CancellationToken token = default);
}

public sealed class JsonContributionUploadStateStore : IContributionUploadStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonContributionUploadStateStore(IConfiguration configuration)
        : this(Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "upload-state-v1.json"))
    {
    }

    public JsonContributionUploadStateStore(string path)
    {
        _path = path;
    }

    public async Task<ContributionUploadStateV1> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new ContributionUploadStateV1();
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ContributionUploadStateV1>(stream, JsonOptions, token).ConfigureAwait(false)
                   ?? new ContributionUploadStateV1();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ContributionUploadStateV1 state, CancellationToken token = default)
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
