using RensaioBackend.Services.Contributions;
using System.Text.Json;
using Xunit;

namespace RensaioBackend.Tests;

public sealed class ContributionCollectorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rensaio-contribution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Identity_IsStableAndSourceNamespaced()
    {
        string first = ContributionIdentity.Create("pkg.example", 42, "/manga/one");
        string repeated = ContributionIdentity.Create("PKG.EXAMPLE", 42, " /manga/one ");
        string otherSource = ContributionIdentity.Create("pkg.example", 43, "/manga/one");
        string otherPackage = ContributionIdentity.Create("pkg.other", 42, "/manga/one");

        Assert.Equal(first, repeated);
        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, otherSource);
        Assert.NotEqual(first, otherPackage);
    }

    [Fact]
    public async Task CheckpointStore_ReplacesDocumentWithoutLeavingTempFiles()
    {
        string path = Path.Combine(_folder, "checkpoint.json");
        var store = new JsonContributionCheckpointStore(path);
        await store.WriteAsync(new ContributionCheckpointV1
        {
            State = ContributionStates.Running,
            ItemsCollected = 3,
            CompletedAssignments = new HashSet<string>(["pkg|1"], StringComparer.OrdinalIgnoreCase)
        });
        await store.WriteAsync(new ContributionCheckpointV1
        {
            State = ContributionStates.Completed,
            ItemsCollected = 7
        });

        ContributionCheckpointV1 loaded = await store.ReadAsync();
        Assert.Equal(ContributionStates.Completed, loaded.State);
        Assert.Equal(7, loaded.ItemsCollected);
        Assert.Empty(loaded.CompletedAssignments);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task LocalSink_DeduplicatesIdentityAndMergesFeedFlags()
    {
        string path = Path.Combine(_folder, "records.json");
        var sink = new LocalJsonContributionSink(path);
        ContributionRecordV1 popular = Record("id-1", popular: true, latest: false);
        ContributionRecordV1 latest = Record("id-1", popular: false, latest: true);

        Assert.Equal(1, await sink.WriteAsync(new ContributionBatchV1 { Records = [popular] }));
        Assert.Equal(1, await sink.WriteAsync(new ContributionBatchV1 { Records = [latest] }));

        ContributionBatchV1 stored = JsonSerializer.Deserialize<ContributionBatchV1>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ContributionRecordV1 record = Assert.Single(stored.Records);
        Assert.True(record.SeenInPopular);
        Assert.True(record.SeenInLatest);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task InteractiveGate_BlocksUntilLastSweepEnds()
    {
        var gate = new InteractiveDiscoveryGate();
        IDisposable first = gate.Begin();
        IDisposable second = gate.Begin();
        Task idle = gate.WaitUntilIdleAsync();

        first.Dispose();
        Assert.False(idle.IsCompleted);
        second.Dispose();
        await idle.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(gate.IsActive);
    }

    [Fact]
    public void StatusContract_UsesRequiredFieldNames()
    {
        string json = JsonSerializer.Serialize(new ContributionStatusDto
        {
            Enabled = true,
            State = ContributionStates.Completed,
            ItemsCollected = 5
        });
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("enabled", out _));
        Assert.True(root.TryGetProperty("state", out _));
        Assert.True(root.TryGetProperty("lastStartedUtc", out _));
        Assert.True(root.TryGetProperty("lastCompletedUtc", out _));
        Assert.True(root.TryGetProperty("itemsCollected", out _));
        Assert.True(root.TryGetProperty("lastError", out _));
    }

    private static ContributionRecordV1 Record(string id, bool popular, bool latest) => new()
    {
        Id = id,
        Package = "pkg",
        SourceId = 1,
        SourceName = "Source",
        SourceLanguage = "en",
        Url = "/one",
        Title = "One",
        SeenInPopular = popular,
        SeenInLatest = latest
    };

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }
}
