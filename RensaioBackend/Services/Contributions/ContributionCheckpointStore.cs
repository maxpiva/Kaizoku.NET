using System.Text.Json;

namespace RensaioBackend.Services.Contributions;

public sealed class ContributionCheckpointV1
{
    public int Version { get; set; } = 1;
    public string State { get; set; } = ContributionStates.Idle;
    public DateTime? LastStartedUtc { get; set; }
    public DateTime? LastCompletedUtc { get; set; }
    public int ItemsCollected { get; set; }
    public string? LastError { get; set; }
    public HashSet<string> CompletedAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IContributionCheckpointStore
{
    Task<ContributionCheckpointV1> ReadAsync(CancellationToken token = default);
    Task WriteAsync(ContributionCheckpointV1 checkpoint, CancellationToken token = default);
}

public sealed class JsonContributionCheckpointStore : IContributionCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonContributionCheckpointStore(IConfiguration configuration)
        : this(Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "checkpoint-v1.json"))
    {
    }

    public JsonContributionCheckpointStore(string path)
    {
        _path = path;
    }

    public async Task<ContributionCheckpointV1> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new ContributionCheckpointV1();
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ContributionCheckpointV1>(stream, JsonOptions, token).ConfigureAwait(false)
                   ?? new ContributionCheckpointV1();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ContributionCheckpointV1 checkpoint, CancellationToken token = default)
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
                    await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, token).ConfigureAwait(false);
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
