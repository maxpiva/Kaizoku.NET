using System.Text.Json;

namespace RensaioBackend.Services.Contributions;

public interface IContributionSink
{
    Task<int> WriteAsync(ContributionBatchV1 batch, CancellationToken token = default);
}

public sealed class LocalJsonContributionSink : IContributionSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalJsonContributionSink(IConfiguration configuration)
        : this(Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "contributions-v1.json"))
    {
    }

    public LocalJsonContributionSink(string path)
    {
        _path = path;
    }

    public async Task<int> WriteAsync(ContributionBatchV1 batch, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ContributionBatchV1 current = await ReadUnsafeAsync(token).ConfigureAwait(false);
            var records = current.Records.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (ContributionRecordV1 incoming in batch.Records)
            {
                if (records.TryGetValue(incoming.Id, out ContributionRecordV1? previous))
                {
                    records[incoming.Id] = Merge(previous, incoming);
                }
                else
                {
                    records[incoming.Id] = incoming;
                }
            }

            var output = new ContributionBatchV1
            {
                GeneratedUtc = batch.GeneratedUtc,
                Records = records.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList()
            };
            await WriteAtomicUnsafeAsync(output, token).ConfigureAwait(false);
            return batch.Records.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ContributionBatchV1> ReadUnsafeAsync(CancellationToken token)
    {
        if (!File.Exists(_path))
            return new ContributionBatchV1();
        await using FileStream stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<ContributionBatchV1>(stream, JsonOptions, token).ConfigureAwait(false)
               ?? new ContributionBatchV1();
    }

    private async Task WriteAtomicUnsafeAsync(ContributionBatchV1 batch, CancellationToken token)
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
                await JsonSerializer.SerializeAsync(stream, batch, JsonOptions, token).ConfigureAwait(false);
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

    private static ContributionRecordV1 Merge(ContributionRecordV1 previous, ContributionRecordV1 incoming)
    {
        return new ContributionRecordV1
        {
            Id = incoming.Id,
            Package = incoming.Package,
            SourceId = incoming.SourceId,
            SourceName = incoming.SourceName,
            SourceLanguage = incoming.SourceLanguage,
            Url = incoming.Url,
            RealUrl = incoming.RealUrl,
            Title = incoming.Title,
            ThumbnailUrl = incoming.ThumbnailUrl,
            Author = incoming.Author,
            Artist = incoming.Artist,
            Description = incoming.Description,
            Genre = incoming.Genre,
            Status = incoming.Status,
            SeenInPopular = previous.SeenInPopular || incoming.SeenInPopular,
            SeenInLatest = previous.SeenInLatest || incoming.SeenInLatest
        };
    }
}
