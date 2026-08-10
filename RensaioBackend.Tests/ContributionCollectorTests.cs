using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Settings;
using System.Reflection;
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

    [Fact]
    public async Task RunAsync_Cancellation_LeavesResumableNonRunningState()
    {
        // ContributionCollector reads settings through SettingsService's static cache; seed it
        // via reflection so the run reaches the checkpoint writes without any infrastructure.
        FieldInfo settingsField = typeof(SettingsService)
            .GetField("_settings", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previousSettings = settingsField.GetValue(null);
        settingsField.SetValue(null, new SettingsDto { ContributionCollectorEnabled = true });
        try
        {
            var store = new CancelOnRunningWriteCheckpointStore(new ContributionCheckpointV1
            {
                State = ContributionStates.Yielding,
                ItemsCollected = 2,
                LastStartedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CompletedAssignments = new HashSet<string>(["pkg|1"], StringComparer.OrdinalIgnoreCase)
            });
            var collector = new ContributionCollector(
                null!, new SettingsService(null!, null!, null!), null!, null!, store,
                new InteractiveDiscoveryGate(), NullLogger<ContributionCollector>.Instance);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.RunAsync());

            Assert.Equal(ContributionStates.Queued, store.Current.State);
            Assert.Equal(2, store.Current.ItemsCollected);
            Assert.Contains("pkg|1", store.Current.CompletedAssignments);
        }
        finally
        {
            settingsField.SetValue(null, previousSettings);
        }
    }

    /// <summary>
    /// In-memory checkpoint store that simulates a shutdown by throwing
    /// <see cref="OperationCanceledException"/> the moment a "running" state is persisted.
    /// </summary>
    private sealed class CancelOnRunningWriteCheckpointStore : IContributionCheckpointStore
    {
        public ContributionCheckpointV1 Current { get; private set; }

        public CancelOnRunningWriteCheckpointStore(ContributionCheckpointV1 initial) => Current = initial;

        public Task<ContributionCheckpointV1> ReadAsync(CancellationToken token = default)
            => Task.FromResult(Clone(Current));

        public Task WriteAsync(ContributionCheckpointV1 checkpoint, CancellationToken token = default)
        {
            if (checkpoint.State == ContributionStates.Running)
                throw new OperationCanceledException("Simulated shutdown while persisting the running state.");
            Current = Clone(checkpoint);
            return Task.CompletedTask;
        }

        private static ContributionCheckpointV1 Clone(ContributionCheckpointV1 checkpoint) => new()
        {
            Version = checkpoint.Version,
            State = checkpoint.State,
            LastStartedUtc = checkpoint.LastStartedUtc,
            LastCompletedUtc = checkpoint.LastCompletedUtc,
            ItemsCollected = checkpoint.ItemsCollected,
            LastError = checkpoint.LastError,
            CompletedAssignments = new HashSet<string>(checkpoint.CompletedAssignments, StringComparer.OrdinalIgnoreCase)
        };
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
