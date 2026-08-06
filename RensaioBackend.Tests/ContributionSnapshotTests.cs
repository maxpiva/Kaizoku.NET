using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Contributions.Snapshot;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Settings;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

// SettingsService caches settings in a static field that ContributionUploadTests,
// ContributionCollectorTests, and this file all seed/restore via reflection (the established
// SettingsScope pattern). xUnit's default per-class-collection parallelism otherwise races two
// classes' reflection writes to that single shared field — reproducible even on main by running
// the suite a few times before this file existed. Disabling collection parallelization for the
// whole assembly (an assembly-level attribute is valid in any file) removes the race without
// touching the other test files.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RensaioBackend.Tests;

/// <summary>
/// Milestone 3: the contribution snapshot download job (titles.json + sources.json +
/// metadata.json fetched from a public export, decrypted with the worker's public /key,
/// decoded into snapshot-v1.json). Mirrors <see cref="ContributionUploadTests"/>'s scaffolding
/// (FakeHandler/FakeFactory/SettingsScope/CapturingLogger), extended with a path-routing fake
/// handler since a run touches four distinct URLs.
/// </summary>
public sealed class ContributionSnapshotTests : IDisposable
{
    private const string SnapshotBaseUrl = "https://snapshot.test";
    private const string WorkerUrl = "https://contribution.test";

    private static readonly TimeSpan[] FastBackoff =
        [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    // Fixed, deterministic 32-byte key / 16-byte IV — the crypto fixtures round-trip through the
    // real production Encode/Decrypt/Decode path, never a golden ciphertext blob.
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 1)).ToArray();
    private static readonly byte[] Iv = Enumerable.Range(0, 16).Select(i => (byte)(i * 3 + 11)).ToArray();
    private static readonly byte[] WrongKey = Enumerable.Repeat((byte)0xAA, 32).ToArray();

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rensaio-snapshot-tests", Guid.NewGuid().ToString("N"));

    private string SnapshotPath => Path.Combine(_folder, "snapshot-v1.json");
    private string StatePath => Path.Combine(_folder, "snapshot-state.json");

    // --- 1. Key parse ---

    [Fact]
    public async Task GetKey_Accepts48Bytes_SplitsKeyAndIv()
    {
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());
        var client = new ContributionSnapshotClient(new FakeFactory(handler), NullLogger<ContributionSnapshotClient>.Instance);

        SnapshotKeyResult result = await client.GetKeyAsync(WorkerUrl);

        Assert.True(result.Success);
        Assert.Equal(Key, result.Key);
        Assert.Equal(Iv, result.Iv);
    }

    [Fact]
    public async Task GetKey_Rejects47Bytes()
    {
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/key", HttpStatusCode.OK, Convert.ToBase64String(new byte[47]));
        var client = new ContributionSnapshotClient(new FakeFactory(handler), NullLogger<ContributionSnapshotClient>.Instance);

        SnapshotKeyResult result = await client.GetKeyAsync(WorkerUrl);

        Assert.False(result.Success);
        Assert.Null(result.Key);
        Assert.Contains("47", result.Error);
    }

    [Fact]
    public async Task GetKey_Rejects49Bytes()
    {
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/key", HttpStatusCode.OK, Convert.ToBase64String(new byte[49]));
        var client = new ContributionSnapshotClient(new FakeFactory(handler), NullLogger<ContributionSnapshotClient>.Instance);

        SnapshotKeyResult result = await client.GetKeyAsync(WorkerUrl);

        Assert.False(result.Success);
        Assert.Contains("49", result.Error);
    }

    [Fact]
    public async Task GetKey_RejectsInvalidBase64()
    {
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/key", HttpStatusCode.OK, "not*valid*base64");
        var client = new ContributionSnapshotClient(new FakeFactory(handler), NullLogger<ContributionSnapshotClient>.Instance);

        SnapshotKeyResult result = await client.GetKeyAsync(WorkerUrl);

        Assert.False(result.Success);
        Assert.Contains("base64", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // --- 2. Crypto round-trip ---

    [Fact]
    public void Crypto_DecryptThenEnvelopeDecode_RoundTripsThePayload()
    {
        ContributionBlobPayloadV1 payload = Payload("internal-1", "One Piece", "/manga/one");
        string base64Cipher = EncryptPayload(payload);
        byte[] cipher = Convert.FromBase64String(base64Cipher);

        byte[] envelope = ContributionSnapshotCrypto.Decrypt(Key, Iv, cipher);
        ContributionBlobPayloadV1 decoded = ContributionBlobEnvelope.Decode(envelope);

        AssertPayloadEqual(payload, decoded);
    }

    // --- 3. Happy path ---

    [Fact]
    public async Task Run_HappyPath_WritesByteCorrectSnapshotWithRightCountersAndETags()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId1 = Guid.NewGuid().ToString();
        string titleId2 = Guid.NewGuid().ToString();
        ContributionBlobPayloadV1 payload1 = Payload("i1", "Title One", "/manga/one");
        ContributionBlobPayloadV1 payload2 = Payload("i2", "Title Two", "/manga/two");
        ContributionBlobPayloadV1 payload3 = Payload("i3", "Title Three", "/manga/three");
        string titlesJson = TitlesJson((titleId1, "Title One"), (titleId2, "Title Two"));
        string sourcesJson = SourcesJson(
            (titleId1, Guid.NewGuid().ToString("N"), EncryptPayload(payload1)),
            (titleId2, Guid.NewGuid().ToString("N"), EncryptPayload(payload2)),
            (titleId1, Guid.NewGuid().ToString("N"), EncryptPayload(payload3)));
        string metadataJson = MetadataJson((titleId1, "mangadex", "abc-123", 0));

        var handler = new RoutingFakeHandler();
        handler.Enqueue("/titles.json", HttpStatusCode.OK, titlesJson, "\"t1\"");
        handler.Enqueue("/sources.json", HttpStatusCode.OK, sourcesJson, "\"s1\"");
        handler.Enqueue("/metadata.json", HttpStatusCode.OK, metadataJson, "\"m1\"");
        handler.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());
        var store = new JsonContributionSnapshotStateStore(StatePath);

        await NewDownloader(handler, store).RunAsync();

        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
        Assert.False(state.LastRunUnchanged);
        Assert.Equal(2, state.Titles);
        Assert.Equal(3, state.RecordsDecoded);
        Assert.Equal(0, state.RecordsNullData);
        Assert.Equal(0, state.RecordsFailed);
        Assert.Equal(0, state.DanglingTitleRefs);
        Assert.Equal(1, state.MetadataLinks);
        Assert.Null(state.LastError);
        Assert.Equal("\"t1\"", state.ETags["titles.json"]);
        Assert.Equal("\"s1\"", state.ETags["sources.json"]);
        Assert.Equal("\"m1\"", state.ETags["metadata.json"]);

        ContributionSnapshotV1 snapshot = ReadSnapshot();
        Assert.Equal(2, snapshot.Titles.Count);
        Assert.Equal(titleId1, snapshot.Titles[0].Id);
        Assert.Equal("Title One", snapshot.Titles[0].Title);
        Assert.Equal(3, snapshot.Records.Count);
        AssertPayloadEqual(payload1, snapshot.Records[0].Payload);
        AssertPayloadEqual(payload2, snapshot.Records[1].Payload);
        AssertPayloadEqual(payload3, snapshot.Records[2].Payload);
        Assert.All(snapshot.Records, r => Assert.False(r.TitleIdDangling));
        Assert.Single(snapshot.Metadata);
        Assert.Equal("mangadex", snapshot.Metadata[0].Provider);
        Assert.Equal("abc-123", snapshot.Metadata[0].ProviderKey);
    }

    // --- 4. All three 304 on second run ---

    [Fact]
    public async Task Run_AllThree304_MakesNoKeyRequest_AndLeavesSnapshotUnmodified()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var store = new JsonContributionSnapshotStateStore(StatePath);
        var first = new RoutingFakeHandler();
        EnqueueFullRun(first,
            TitlesJson((Guid.NewGuid().ToString(), "A")),
            SourcesJson(),
            MetadataJson());
        await NewDownloader(first, store).RunAsync();
        byte[] bytesAfterFirst = await File.ReadAllBytesAsync(SnapshotPath);

        var second = new RoutingFakeHandler();
        second.Enqueue("/titles.json", HttpStatusCode.NotModified);
        second.Enqueue("/sources.json", HttpStatusCode.NotModified);
        second.Enqueue("/metadata.json", HttpStatusCode.NotModified);
        await NewDownloader(second, store).RunAsync();

        Assert.DoesNotContain(second.Requests, r => r.Uri.AbsolutePath == "/key");
        Assert.Equal(3, second.Requests.Count);
        byte[] bytesAfterSecond = await File.ReadAllBytesAsync(SnapshotPath);
        Assert.Equal(bytesAfterFirst, bytesAfterSecond);

        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.True(state.LastRunUnchanged);
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
    }

    // --- 5. One file changed: the 304'd files are re-requested without If-None-Match ---

    [Fact]
    public async Task Run_OneFileChanged_ReRequests304dFilesWithoutConditionalHeader()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var store = new JsonContributionSnapshotStateStore(StatePath);
        string titleId = Guid.NewGuid().ToString();
        var first = new RoutingFakeHandler();
        EnqueueFullRun(first, TitlesJson((titleId, "A")), SourcesJson(), MetadataJson());
        await NewDownloader(first, store).RunAsync();

        var second = new RoutingFakeHandler();
        second.Enqueue("/titles.json", HttpStatusCode.NotModified);
        second.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t2\"");
        second.Enqueue("/sources.json", HttpStatusCode.OK,
            SourcesJson((titleId, Guid.NewGuid().ToString("N"), EncryptPayload(Payload("i1", "New", "/manga/new")))), "\"s2\"");
        second.Enqueue("/metadata.json", HttpStatusCode.NotModified);
        second.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m2\"");
        second.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());

        await NewDownloader(second, store).RunAsync();

        List<CapturedRequest> titleRequests = second.Requests.Where(r => r.Uri.AbsolutePath == "/titles.json").ToList();
        Assert.Equal(2, titleRequests.Count);
        Assert.NotNull(titleRequests[0].IfNoneMatch);
        Assert.Null(titleRequests[1].IfNoneMatch);

        List<CapturedRequest> metadataRequests = second.Requests.Where(r => r.Uri.AbsolutePath == "/metadata.json").ToList();
        Assert.Equal(2, metadataRequests.Count);
        Assert.NotNull(metadataRequests[0].IfNoneMatch);
        Assert.Null(metadataRequests[1].IfNoneMatch);

        List<CapturedRequest> sourceRequests = second.Requests.Where(r => r.Uri.AbsolutePath == "/sources.json").ToList();
        Assert.Single(sourceRequests);

        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
        Assert.False(state.LastRunUnchanged);
        ContributionSnapshotV1 snapshot = ReadSnapshot();
        Assert.Single(snapshot.Records);
        Assert.Equal("New", snapshot.Records[0].Payload.Title);
    }

    // --- 6. data:null skipped ---

    [Fact]
    public async Task Run_NullDataRow_IsSkippedAndCounted()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        EnqueueFullRun(handler,
            TitlesJson((titleId, "A")),
            SourcesJson((titleId, Guid.NewGuid().ToString("N"), null)),
            MetadataJson());

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
        Assert.Equal(1, state.RecordsNullData);
        Assert.Equal(0, state.RecordsDecoded);
        Assert.Equal(0, state.RecordsFailed);
    }

    // --- 7. Three malformed classes; good records survive ---

    [Fact]
    public async Task Run_ThreeMalformedRecordClasses_GoodRecordsSurviveAndFailedIsThree()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        ContributionBlobPayloadV1 good1 = Payload("g1", "Good One", "/manga/good1");
        ContributionBlobPayloadV1 good2 = Payload("g2", "Good Two", "/manga/good2");
        byte[] garbageEnvelope = { 0x99, 0x01, 0x02, 0x03 };

        string sourcesJson = SourcesJson(
            (titleId, Guid.NewGuid().ToString("N"), EncryptPayload(good1)),
            (titleId, Guid.NewGuid().ToString("N"), "not*valid*base64"),
            (titleId, Guid.NewGuid().ToString("N"), EncryptBytes(ContributionBlobEnvelope.Encode(good2), Key, Iv)),
            (titleId, Guid.NewGuid().ToString("N"), EncryptBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, WrongKey, Iv)),
            (titleId, Guid.NewGuid().ToString("N"), EncryptBytes(garbageEnvelope, Key, Iv)));

        var handler = new RoutingFakeHandler();
        EnqueueFullRun(handler, TitlesJson((titleId, "A")), sourcesJson, MetadataJson());

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
        Assert.Equal(2, state.RecordsDecoded);
        Assert.Equal(3, state.RecordsFailed);

        ContributionSnapshotV1 snapshot = ReadSnapshot();
        Assert.Equal(2, snapshot.Records.Count);
        List<string> titles = snapshot.Records.Select(r => r.Payload.Title).ToList();
        Assert.Contains("Good One", titles);
        Assert.Contains("Good Two", titles);
    }

    // --- 8. Dangling title_id ---

    [Fact]
    public async Task Run_DanglingTitleId_RecordKeptAndFlagged()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string realTitleId = Guid.NewGuid().ToString();
        string danglingTitleId = Guid.NewGuid().ToString();
        ContributionBlobPayloadV1 payload = Payload("i1", "Orphan", "/manga/orphan");
        var handler = new RoutingFakeHandler();
        EnqueueFullRun(handler,
            TitlesJson((realTitleId, "Real")),
            SourcesJson((danglingTitleId, Guid.NewGuid().ToString("N"), EncryptPayload(payload))),
            MetadataJson());

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(1, state.DanglingTitleRefs);
        Assert.Equal(1, state.RecordsDecoded);
        ContributionSnapshotV1 snapshot = ReadSnapshot();
        ContributionSnapshotRecordV1 record = Assert.Single(snapshot.Records);
        Assert.True(record.TitleIdDangling);
        Assert.Equal(danglingTitleId, record.TitleId);
    }

    // --- 9. Duplicate title strings kept; duplicate title ids last-wins ---

    [Fact]
    public async Task Run_DuplicateTitleStringsKept_DuplicateIdsLastWins()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string idA = Guid.NewGuid().ToString();
        string idB = Guid.NewGuid().ToString();
        string dupId = Guid.NewGuid().ToString();
        // Two distinct ids share a title string (kept); dupId appears twice with different
        // titles (raw rows both kept, but the dangling lookup uses last-occurrence-wins).
        string titlesJson = TitlesJson(
            (idA, "Same Title"), (idB, "Same Title"),
            (dupId, "First"), (dupId, "Second"));
        ContributionBlobPayloadV1 payload = Payload("i1", "Linked", "/manga/linked");
        var logs = new List<string>();
        var handler = new RoutingFakeHandler();
        EnqueueFullRun(handler, titlesJson,
            SourcesJson((dupId, Guid.NewGuid().ToString("N"), EncryptPayload(payload))),
            MetadataJson());

        await NewDownloader(handler, logger: new CapturingLogger<ContributionSnapshotDownloader>(logs)).RunAsync();

        ContributionSnapshotV1 snapshot = ReadSnapshot();
        Assert.Equal(4, snapshot.Titles.Count);
        Assert.Contains(snapshot.Titles, t => t.Id == idA && t.Title == "Same Title");
        Assert.Contains(snapshot.Titles, t => t.Id == idB && t.Title == "Same Title");
        Assert.Equal(2, snapshot.Titles.Count(t => t.Id == dupId));

        ContributionSnapshotRecordV1 record = Assert.Single(snapshot.Records);
        Assert.False(record.TitleIdDangling); // dupId resolves (last-wins keeps it present)
        Assert.Contains(logs, line => line.Contains("duplicate ids", StringComparison.OrdinalIgnoreCase));
    }

    // --- 10. All records fail decrypt while a previous good snapshot exists ---

    [Fact]
    public async Task Run_AllRecordsFailDecrypt_PreservesPreviousSnapshotAndFails()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var store = new JsonContributionSnapshotStateStore(StatePath);
        string titleId = Guid.NewGuid().ToString();
        var first = new RoutingFakeHandler();
        EnqueueFullRun(first,
            TitlesJson((titleId, "A")),
            SourcesJson((titleId, Guid.NewGuid().ToString("N"), EncryptPayload(Payload("i1", "Good", "/manga/good")))),
            MetadataJson());
        await NewDownloader(first, store).RunAsync();
        byte[] bytesAfterFirst = await File.ReadAllBytesAsync(SnapshotPath);

        // Second run: served key doesn't match what the sources were encrypted with (server
        // rotated the key without a matching export regeneration).
        var second = new RoutingFakeHandler();
        second.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t2\"");
        second.Enqueue("/sources.json", HttpStatusCode.OK,
            SourcesJson((titleId, Guid.NewGuid().ToString("N"), EncryptPayload(Payload("i1", "Good", "/manga/good")))), "\"s2\"");
        second.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m2\"");
        second.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64(WrongKey, Iv));

        await NewDownloader(second, store).RunAsync();

        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Failed, state.State);
        Assert.NotNull(state.LastError);
        byte[] bytesAfterSecond = await File.ReadAllBytesAsync(SnapshotPath);
        Assert.Equal(bytesAfterFirst, bytesAfterSecond);
    }

    // --- 11. Malformed titles.json ---

    [Fact]
    public async Task Run_MalformedTitlesJson_FailsAndLeavesPreviousSnapshotIntact()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var store = new JsonContributionSnapshotStateStore(StatePath);
        string titleId = Guid.NewGuid().ToString();
        var first = new RoutingFakeHandler();
        EnqueueFullRun(first, TitlesJson((titleId, "A")), SourcesJson(), MetadataJson());
        await NewDownloader(first, store).RunAsync();
        byte[] bytesAfterFirst = await File.ReadAllBytesAsync(SnapshotPath);

        var second = new RoutingFakeHandler();
        second.Enqueue("/titles.json", HttpStatusCode.OK, "{ not valid json ]", "\"t2\"");
        second.Enqueue("/sources.json", HttpStatusCode.OK, SourcesJson(), "\"s2\"");
        second.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m2\"");
        second.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());

        await NewDownloader(second, store).RunAsync();

        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Failed, state.State);
        Assert.Contains("titles.json", state.LastError);
        byte[] bytesAfterSecond = await File.ReadAllBytesAsync(SnapshotPath);
        Assert.Equal(bytesAfterFirst, bytesAfterSecond);
    }

    // --- 12. Retry / backoff ---

    [Fact]
    public async Task Run_OneRetryableErrorThenSuccess_RetriesAndCompletes()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t1\"");
        handler.Enqueue("/sources.json", HttpStatusCode.InternalServerError, "boom");
        handler.Enqueue("/sources.json", HttpStatusCode.OK, SourcesJson(), "\"s1\"");
        handler.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m1\"");
        handler.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());

        await NewDownloader(handler).RunAsync();

        Assert.Equal(2, handler.Requests.Count(r => r.Uri.AbsolutePath == "/sources.json"));
        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
    }

    [Fact]
    public async Task Run_ThreeConsecutiveRetryableErrors_Fails()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t1\"");
        handler.Enqueue("/sources.json", HttpStatusCode.InternalServerError, "boom");
        handler.Enqueue("/sources.json", HttpStatusCode.InternalServerError, "boom");
        handler.Enqueue("/sources.json", HttpStatusCode.InternalServerError, "boom");
        handler.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m1\"");

        await NewDownloader(handler).RunAsync();

        Assert.Equal(3, handler.Requests.Count(r => r.Uri.AbsolutePath == "/sources.json"));
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath == "/key");
        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Failed, state.State);
        Assert.NotNull(state.LastError);
        Assert.False(File.Exists(SnapshotPath));
    }

    // --- 13. 404 tolerance (metadata) vs failure (sources) ---

    [Fact]
    public async Task Run_MetadataNotFound_IsToleratedAsEmpty_AndCompletes()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t1\"");
        handler.Enqueue("/sources.json", HttpStatusCode.OK, SourcesJson(), "\"s1\"");
        handler.Enqueue("/metadata.json", HttpStatusCode.NotFound);
        handler.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
        Assert.Equal(0, state.MetadataLinks);
        Assert.DoesNotContain("metadata.json", (IEnumerable<string>)state.ETags.Keys);
        ContributionSnapshotV1 snapshot = ReadSnapshot();
        Assert.Empty(snapshot.Metadata);
    }

    [Fact]
    public async Task Run_SourcesNotFound_Fails()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        handler.Enqueue("/titles.json", HttpStatusCode.OK, TitlesJson((titleId, "A")), "\"t1\"");
        handler.Enqueue("/sources.json", HttpStatusCode.NotFound);
        handler.Enqueue("/metadata.json", HttpStatusCode.OK, MetadataJson(), "\"m1\"");
        handler.Enqueue("/key", HttpStatusCode.OK, KeyMaterialBase64());

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Failed, state.State);
        Assert.Contains("sources.json", state.LastError);
    }

    // --- 14. Disabled ---

    [Fact]
    public async Task Run_Disabled_SetsDisabledStateAndMakesNoRequests()
    {
        using SettingsScope scope = SettingsScope.Enable();
        scope.Set(s => s.ContributionSnapshotEnabled = false);
        var handler = new RoutingFakeHandler();

        await NewDownloader(handler).RunAsync();

        ContributionSnapshotStateV1 state = await new JsonContributionSnapshotStateStore(StatePath).ReadAsync();
        Assert.Equal(ContributionSnapshotStates.Disabled, state.State);
        Assert.Empty(handler.Requests);
    }

    // --- 15. SnapshotUrl change clears ETags ---

    [Fact]
    public async Task Run_SnapshotUrlChanges_ClearsETags_NoConditionalHeaderSent()
    {
        using SettingsScope scope = SettingsScope.Enable();
        var store = new JsonContributionSnapshotStateStore(StatePath);
        string titleId = Guid.NewGuid().ToString();
        var first = new RoutingFakeHandler();
        EnqueueFullRun(first, TitlesJson((titleId, "A")), SourcesJson(), MetadataJson());
        await NewDownloader(first, store).RunAsync();
        Assert.NotEmpty((await store.ReadAsync()).ETags);

        scope.Set(s => s.ContributionSnapshotUrl = "https://snapshot2.test");
        var second = new RoutingFakeHandler();
        EnqueueFullRun(second, TitlesJson((titleId, "A")), SourcesJson(), MetadataJson());
        await NewDownloader(second, store).RunAsync();

        Assert.All(second.Requests.Where(r => r.Uri.AbsolutePath != "/key"), r => Assert.Null(r.IfNoneMatch));
        ContributionSnapshotStateV1 state = await store.ReadAsync();
        Assert.Equal("https://snapshot2.test", state.SnapshotUrl);
        Assert.Equal(ContributionSnapshotStates.Completed, state.State);
    }

    // --- 16. Empty contributor UUID: no credential leak ---

    [Fact]
    public async Task Run_EmptyContributorUuid_NeverAppearsInRequestsOrLogs()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string titleId = Guid.NewGuid().ToString();
        var handler = new RoutingFakeHandler();
        EnqueueFullRun(handler,
            TitlesJson((titleId, "A")),
            SourcesJson((titleId, Guid.NewGuid().ToString("N"), EncryptPayload(Payload("i1", "T", "/u")))),
            MetadataJson());
        var logs = new List<string>();

        await NewDownloader(handler, logger: new CapturingLogger<ContributionSnapshotDownloader>(logs)).RunAsync();

        Assert.NotEmpty(handler.Requests);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.ToString().Contains("contributor", StringComparison.OrdinalIgnoreCase));
        var uuidPattern = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        Assert.DoesNotContain(logs, line => uuidPattern.IsMatch(line));
        Assert.DoesNotContain(logs, line => line.Contains("contributor", StringComparison.OrdinalIgnoreCase));
    }

    // --- 17. Settings round-trip ---

    [Fact]
    public void SnapshotSettings_PersistenceRoundTrips()
    {
        var original = new EditableSettingsDto
        {
            ContributionSnapshotEnabled = true,
            ContributionSnapshotUrl = "https://snapshot.rensaio.net/export"
        };
        List<SettingEntity> persisted = (List<SettingEntity>)
            typeof(SettingsService).GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [original])!;
        var restoredTuple = ((bool, EditableSettingsDto))typeof(SettingsService)
            .GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [persisted, new EditableSettingsDto()])!;

        Assert.True(restoredTuple.Item2.ContributionSnapshotEnabled);
        Assert.Equal("https://snapshot.rensaio.net/export", restoredTuple.Item2.ContributionSnapshotUrl);
    }

    // --- Helpers ---

    private ContributionSnapshotDownloader NewDownloader(RoutingFakeHandler handler,
        IContributionSnapshotStateStore? store = null, ILogger<ContributionSnapshotDownloader>? logger = null,
        TimeSpan[]? backoff = null)
        => new(
            new SettingsService(EmptyConfig(), null!, null!),
            new ContributionSnapshotClient(new FakeFactory(handler), NullLogger<ContributionSnapshotClient>.Instance),
            store ?? new JsonContributionSnapshotStateStore(StatePath),
            SnapshotPath,
            logger ?? NullLogger<ContributionSnapshotDownloader>.Instance,
            backoff ?? FastBackoff);

    private static void EnqueueFullRun(RoutingFakeHandler handler,
        string titlesBody, string sourcesBody, string metadataBody,
        string titlesEtag = "\"t1\"", string sourcesEtag = "\"s1\"", string metadataEtag = "\"m1\"",
        string? keyBody = null)
    {
        handler.Enqueue("/titles.json", HttpStatusCode.OK, titlesBody, titlesEtag);
        handler.Enqueue("/sources.json", HttpStatusCode.OK, sourcesBody, sourcesEtag);
        handler.Enqueue("/metadata.json", HttpStatusCode.OK, metadataBody, metadataEtag);
        handler.Enqueue("/key", HttpStatusCode.OK, keyBody ?? KeyMaterialBase64());
    }

    private ContributionSnapshotV1 ReadSnapshot()
        => JsonSerializer.Deserialize<ContributionSnapshotV1>(File.ReadAllText(SnapshotPath), WireOptions)!;

    private static ContributionBlobPayloadV1 Payload(string id, string title, string url) => new()
    {
        Id = id,
        Package = "pkg.example",
        SourceId = 42,
        SourceName = "Example",
        SourceLanguage = "en",
        Url = url,
        Title = title,
        ObservedUtc = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc)
    };

    private static void AssertPayloadEqual(ContributionBlobPayloadV1 expected, ContributionBlobPayloadV1 actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Package, actual.Package);
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.SourceName, actual.SourceName);
        Assert.Equal(expected.SourceLanguage, actual.SourceLanguage);
        Assert.Equal(expected.Url, actual.Url);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.ObservedUtc, actual.ObservedUtc);
    }

    private static string EncryptPayload(ContributionBlobPayloadV1 payload, byte[]? key = null, byte[]? iv = null)
        => EncryptBytes(ContributionBlobEnvelope.Encode(payload), key ?? Key, iv ?? Iv);

    private static string EncryptBytes(byte[] plaintext, byte[] key, byte[] iv)
    {
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        return Convert.ToBase64String(cipher);
    }

    private static string KeyMaterialBase64(byte[]? key = null, byte[]? iv = null)
        => Convert.ToBase64String((key ?? Key).Concat(iv ?? Iv).ToArray());

    private static string TitlesJson(params (string Id, string Title)[] rows)
        => JsonSerializer.Serialize(rows.Select(r => new SnapshotTitleRow { Id = r.Id, Title = r.Title }).ToList(), WireOptions);

    private static string SourcesJson(params (string TitleId, string Id, string? Data)[] rows)
        => JsonSerializer.Serialize(rows.Select(r => new SnapshotSourceRow { TitleId = r.TitleId, Id = r.Id, Data = r.Data }).ToList(), WireOptions);

    private static string MetadataJson(params (string TitleId, string Provider, string ProviderKey, int Type)[] rows)
        => JsonSerializer.Serialize(rows.Select(r => new SnapshotMetadataRow
        {
            TitleId = r.TitleId,
            Provider = r.Provider,
            ProviderKey = r.ProviderKey,
            Type = r.Type
        }).ToList(), WireOptions);

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    /// <summary>
    /// Seeds SettingsService's static settings cache with the snapshot feature enabled and a
    /// contributor-credential-free configuration, restoring the previous value on dispose.
    /// </summary>
    private sealed class SettingsScope : IDisposable
    {
        private static readonly FieldInfo Field =
            typeof(SettingsService).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _previous;
        private readonly SettingsDto _current;

        private SettingsScope(SettingsDto settings)
        {
            _previous = Field.GetValue(null);
            _current = settings;
            Field.SetValue(null, settings);
        }

        public static SettingsScope Enable() => new(new SettingsDto
        {
            ContributionSnapshotEnabled = true,
            ContributionSnapshotUrl = SnapshotBaseUrl,
            ContributionUploadUrl = WorkerUrl,
            ContributionContributorUuid = ""
        });

        public void Set(Action<SettingsDto> mutate) => mutate(_current);

        public void Dispose() => Field.SetValue(null, _previous);
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? IfNoneMatch);

    /// <summary>
    /// Path-routing fake handler: a run touches four distinct URLs (titles.json/sources.json/
    /// metadata.json on the snapshot base URL, /key on the worker URL), so responses are scripted
    /// per absolute path rather than as one flat queue.
    /// </summary>
    private sealed class RoutingFakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(string path, Func<HttpResponseMessage> responseFactory)
        {
            if (!_routes.TryGetValue(path, out Queue<Func<HttpResponseMessage>>? queue))
            {
                queue = new Queue<Func<HttpResponseMessage>>();
                _routes[path] = queue;
            }
            queue.Enqueue(responseFactory);
        }

        public void Enqueue(string path, HttpStatusCode status, string? body = null, string? etag = null)
            => Enqueue(path, () =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new ByteArrayContent(body is null ? [] : Encoding.UTF8.GetBytes(body))
                };
                if (etag is not null)
                    response.Headers.TryAddWithoutValidation("ETag", etag);
                return response;
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            string? ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, ifNoneMatch));

            if (!_routes.TryGetValue(path, out Queue<Func<HttpResponseMessage>>? queue) || queue.Count == 0)
                throw new InvalidOperationException($"RoutingFakeHandler received an unscripted request for {path}.");

            return Task.FromResult(queue.Dequeue()());
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly RoutingFakeHandler _handler;
        public FakeFactory(RoutingFakeHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines;
        public CapturingLogger(List<string> lines) => _lines = lines;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }
}
