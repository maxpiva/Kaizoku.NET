using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Contributions;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Settings;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RensaioBackend.Tests;

public sealed class ContributionUploadTests : IDisposable
{
    private const string Uuid = "11111111-2222-3333-4444-555555555555";
    private const string ServerUrl = "https://contribution.test";
    private static readonly TimeSpan[] FastBackoff =
        [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rensaio-upload-tests", Guid.NewGuid().ToString("N"));

    // --- MD5 upload key ---

    [Theory]
    [InlineData("pkg.example", 42L, "/manga/one", "5feaef0114c2d278bf265d43061319ca")]
    [InlineData("eu.kanade.tachiyomi.extension.ja.example", 123L, "/漢画/テスト", "abc3d052c155ff62549873e3e9627989")]
    [InlineData("pkg.ru", -456789L, "/манга/тест", "eeb236a2d945ba37df6cdb4de15b5e1f")]
    [InlineData("pkg", 1L, "/series?id=a:b:c", "d6555d3038d3995936e3dcaf22c7030d")]
    public void UploadKey_MatchesReferenceVectors(string package, long sourceId, string url, string expected)
    {
        // Vectors computed independently (Python hashlib over UTF-8); lowercase hex is the
        // casing convention proposed to the worker side.
        Assert.Equal(expected, ContributionUploadKey.Create(package, sourceId, url));
    }

    // --- Blob envelope ---

    [Fact]
    public void Envelope_BrotliRoundTrips()
    {
        ContributionBlobPayloadV1 payload = Payload("/manga/one", "One Piece", thumbnail: "https://cdn/x.jpg");
        byte[] envelope = ContributionBlobEnvelope.Encode(payload);

        Assert.Equal(ContributionBlobEnvelope.BrotliMarker, envelope[0]);
        ContributionBlobPayloadV1 decoded = ContributionBlobEnvelope.Decode(envelope);
        Assert.Equal(payload.Id, decoded.Id);
        Assert.Equal(payload.Title, decoded.Title);
        Assert.Equal(payload.ThumbnailUrl, decoded.ThumbnailUrl);
        Assert.Equal(payload.SourceId, decoded.SourceId);
        Assert.Equal(payload.ObservedUtc, decoded.ObservedUtc);
    }

    [Fact]
    public void Envelope_DecodesGzipMarker()
    {
        ContributionBlobPayloadV1 payload = Payload("/manga/two", "Two");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var output = new MemoryStream();
        output.WriteByte(ContributionBlobEnvelope.GzipMarker);
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(json);

        ContributionBlobPayloadV1 decoded = ContributionBlobEnvelope.Decode(output.ToArray());
        Assert.Equal(payload.Title, decoded.Title);
        Assert.Equal(payload.Url, decoded.Url);
    }

    [Fact]
    public void Envelope_UnknownMarkerThrows()
    {
        Assert.Throws<InvalidDataException>(() => ContributionBlobEnvelope.Decode([0x03, 0x00, 0x01]));
    }

    [Fact]
    public void PayloadHash_IgnoresObservedUtc_ButTracksContent()
    {
        ContributionBlobPayloadV1 first = Payload("/manga/one", "One", observedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        ContributionBlobPayloadV1 later = Payload("/manga/one", "One", observedUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        ContributionBlobPayloadV1 renamed = Payload("/manga/one", "One!", observedUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(ContributionBlobEnvelope.PayloadHash(first), ContributionBlobEnvelope.PayloadHash(later));
        Assert.NotEqual(ContributionBlobEnvelope.PayloadHash(first), ContributionBlobEnvelope.PayloadHash(renamed));
    }

    // --- Uploader: chunking ---

    [Fact]
    public async Task Run_Chunks127RecordsInto50_50_27()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var handler = new FakeHandler(ContributorOk());
        for (int i = 0; i < 3; i++)
            handler.Enqueue(UploadOk());
        WriteContributions(Enumerable.Range(0, 127).Select(i => Record($"/manga/{i}", $"Title {i}")).ToArray());

        int uploaded = await NewUploader(handler).RunAsync();

        Assert.Equal(127, uploaded);
        List<CapturedRequest> uploads = handler.Requests.Where(r => r.Uri.AbsolutePath == "/upload").ToList();
        Assert.Equal(new[] { 50, 50, 27 }, uploads.Select(r => ItemCount(r.Body)).ToArray());
    }

    [Fact]
    public async Task Run_SourceUploadIncludesExactMihonIdString()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var handler = new FakeHandler(ContributorOk(), UploadOk());
        const long sourceId = 9_007_199_254_740_993L; // First integer JavaScript cannot represent exactly.
        WriteContributions(Record("/manga/one", "One", sourceId: sourceId));

        Assert.Equal(1, await NewUploader(handler).RunAsync());

        CapturedRequest upload = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/upload");
        using JsonDocument document = JsonDocument.Parse(upload.Body);
        JsonElement data = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList())
            .GetProperty("data");
        Assert.Equal(JsonValueKind.String, data.GetProperty("mihonId").ValueKind);
        Assert.Equal("9007199254740993", data.GetProperty("mihonId").GetString());
    }

    [Fact]
    public async Task Run_PreMihonIdDeltaEntryIsUploadedAgain()
    {
        using SettingsScope _ = SettingsScope.Enable();
        ContributionRecordV1 record = Record("/manga/one", "One");
        WriteContributions(record);
        string key = ContributionUploadKey.Create(record);
        string oldHash = ContributionBlobEnvelope.PayloadHash(
            ContributionBlobPayloadV1.FromRecord(record, DateTime.UtcNow));
        var store = new JsonContributionUploadStateStore(Path.Combine(_folder, "upload-state.json"));
        await store.WriteAsync(new ContributionUploadStateV1
        {
            ServerUrl = ServerUrl,
            Entries = new Dictionary<string, ContributionUploadEntryV1>(StringComparer.Ordinal)
            {
                [key] = new() { Hash = oldHash, UploadedUtc = DateTime.UtcNow }
            }
        });
        var handler = new FakeHandler(ContributorOk(), UploadOk());

        Assert.Equal(1, await NewUploader(handler, store).RunAsync());
        Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/upload");
        Assert.StartsWith("source-mihon-id-v1:", (await store.ReadAsync()).Entries[key].Hash);
    }

    [Fact]
    public async Task Run_NoRecords_MakesNoUploadCall()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var handler = new FakeHandler(ContributorOk());
        WriteContributions();

        int uploaded = await NewUploader(handler).RunAsync();

        Assert.Equal(0, uploaded);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath == "/upload");
    }

    // --- Uploader: in-run dedupe ---

    [Fact]
    public async Task Run_DedupesSameUploadKeyLastWins()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var handler = new FakeHandler(ContributorOk(), UploadOk());
        // Distinct internal ids can still collapse to one wire identity; the last record wins
        // so a single batch never carries a duplicate id (worker-side PK violation).
        WriteContributions(
            Record("/manga/dup", "Older", internalId: "internal-a"),
            Record("/manga/dup", "Newer", internalId: "internal-b"));

        int uploaded = await NewUploader(handler).RunAsync();

        Assert.Equal(1, uploaded);
        CapturedRequest upload = Assert.Single(handler.Requests.Where(r => r.Uri.AbsolutePath == "/upload"));
        using JsonDocument document = JsonDocument.Parse(upload.Body);
        JsonElement item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("Newer", item.GetProperty("data").GetProperty("title").GetString());
    }

    // --- Uploader: delta store ---

    [Fact]
    public async Task Run_DeltaSkipsUnchanged_ReuploadsChanged_AndUrlChangeClears()
    {
        using SettingsScope scope = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);

        var first = new FakeHandler(ContributorOk(), UploadOk());
        WriteContributions(Record("/manga/a", "A"), Record("/manga/b", "B"));
        Assert.Equal(2, await NewUploader(first, store).RunAsync());

        // Second run, new file generation time, identical content: everything is skipped and
        // no upload request goes out at all.
        var second = new FakeHandler(ContributorOk());
        WriteContributions(Record("/manga/a", "A"), Record("/manga/b", "B"));
        Assert.Equal(0, await NewUploader(second, store).RunAsync());
        Assert.DoesNotContain(second.Requests, r => r.Uri.AbsolutePath == "/upload");
        ContributionUploadStateV1 afterSecond = await store.ReadAsync();
        Assert.Equal(2, afterSecond.Skipped);
        Assert.Equal(ContributionUploadStates.Completed, afterSecond.State);

        // Content change re-uploads just the changed record.
        var third = new FakeHandler(ContributorOk(), UploadOk());
        WriteContributions(Record("/manga/a", "A"), Record("/manga/b", "B (updated)"));
        Assert.Equal(1, await NewUploader(third, store).RunAsync());
        CapturedRequest thirdUpload = Assert.Single(third.Requests.Where(r => r.Uri.AbsolutePath == "/upload"));
        Assert.Equal(1, ItemCount(thirdUpload.Body));

        // A different worker URL invalidates the delta store: everything uploads again.
        scope.Set(s => s.ContributionUploadUrl = "https://other-worker.test");
        var fourth = new FakeHandler(ContributorOk(), UploadOk());
        Assert.Equal(2, await NewUploader(fourth, store).RunAsync());
    }

    [Fact]
    public async Task Run_MarksOnlyItemsAbsentFromErrors()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        // 200 is not success per item: index 1 fails (the worker's resolveTitle bind bug
        // reports per-item errors exactly like this), the other two are confirmed.
        var handler = new FakeHandler(ContributorOk(),
            Json(HttpStatusCode.OK, """{"processed":2,"skipped":0,"errors":[{"index":1,"message":"D1_ERROR: Wrong number of parameter bindings"}]}"""));
        WriteContributions(Record("/manga/a", "A"), Record("/manga/b", "B"), Record("/manga/c", "C"));

        int uploaded = await NewUploader(handler, store).RunAsync();

        Assert.Equal(2, uploaded);
        ContributionUploadStateV1 state = await store.ReadAsync();
        Assert.Equal(1, state.Failed);
        Assert.Equal(2, state.Entries.Count);
        Assert.Equal(ContributionUploadStates.Completed, state.State);

        // The failed item is retried on the next run; confirmed ones stay skipped.
        var retry = new FakeHandler(ContributorOk(), UploadOk());
        WriteContributions(Record("/manga/a", "A"), Record("/manga/b", "B"), Record("/manga/c", "C"));
        Assert.Equal(1, await NewUploader(retry, store).RunAsync());
        CapturedRequest retryUpload = Assert.Single(retry.Requests.Where(r => r.Uri.AbsolutePath == "/upload"));
        Assert.Equal(1, ItemCount(retryUpload.Body));
    }

    // --- Uploader: backoff and failure ---

    [Fact]
    public async Task Run_RetriesA500ThenSucceeds()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var handler = new FakeHandler(ContributorOk(),
            Json(HttpStatusCode.InternalServerError, """{"error":"batch rolled back"}"""),
            UploadOk());
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(1, await NewUploader(handler).RunAsync());
        Assert.Equal(2, handler.Requests.Count(r => r.Uri.AbsolutePath == "/upload"));
    }

    [Fact]
    public async Task Run_ThreeConsecutive500s_FailsAndLeavesEntriesUntouched()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        var handler = new FakeHandler(ContributorOk(),
            Json(HttpStatusCode.InternalServerError, "{}"),
            Json(HttpStatusCode.InternalServerError, "{}"),
            Json(HttpStatusCode.InternalServerError, "{}"));
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(0, await NewUploader(handler, store).RunAsync());

        Assert.Equal(3, handler.Requests.Count(r => r.Uri.AbsolutePath == "/upload"));
        ContributionUploadStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionUploadStates.Failed, state.State);
        Assert.Empty(state.Entries);
        Assert.NotNull(state.LastError);
    }

    // --- Uploader: contributor states ---

    [Fact]
    public async Task Run_UnknownContributor_SetsInvalidState()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        var handler = new FakeHandler(Json(HttpStatusCode.NotFound, """{"error":"not found"}"""));
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(0, await NewUploader(handler, store).RunAsync());

        Assert.Equal(ContributionUploadStates.Invalid, (await store.ReadAsync()).State);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.AbsolutePath == "/upload");
    }

    [Fact]
    public async Task Run_BannedContributor_SetsBannedStateWithReason()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        var handler = new FakeHandler(
            Json(HttpStatusCode.OK, """{"active":false,"admin":false,"ban_reason":"spam"}"""));
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(0, await NewUploader(handler, store).RunAsync());

        ContributionUploadStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionUploadStates.Banned, state.State);
        Assert.Contains("spam", state.LastError);
    }

    [Fact]
    public async Task Run_403DuringUpload_AbortsAsBanned()
    {
        using SettingsScope _ = SettingsScope.Enable();
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        var handler = new FakeHandler(ContributorOk(),
            Json(HttpStatusCode.Forbidden, """{"error":"Contributor is banned: rate abuse"}"""));
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(0, await NewUploader(handler, store).RunAsync());

        ContributionUploadStateV1 state = await store.ReadAsync();
        Assert.Equal(ContributionUploadStates.Banned, state.State);
        Assert.Contains("rate abuse", state.LastError);
        Assert.Equal(1, handler.Requests.Count(r => r.Uri.AbsolutePath == "/upload"));
    }

    [Fact]
    public async Task Run_MalformedUuid_SetsInvalidWithoutNetworkCalls()
    {
        using SettingsScope scope = SettingsScope.Enable();
        scope.Set(s => s.ContributionContributorUuid = "not-a-uuid");
        string statePath = Path.Combine(_folder, "upload-state.json");
        var store = new JsonContributionUploadStateStore(statePath);
        var handler = new FakeHandler();
        WriteContributions(Record("/manga/a", "A"));

        Assert.Equal(0, await NewUploader(handler, store).RunAsync());

        Assert.Equal(ContributionUploadStates.Invalid, (await store.ReadAsync()).State);
        Assert.Empty(handler.Requests);
    }

    // --- Secrets ---

    [Fact]
    public async Task Run_NeverLogsTheContributorUuid()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var logs = new List<string>();
        // Exercise both a per-item rejection and a transport failure — the noisiest paths.
        var handler = new FakeHandler(ContributorOk(),
            Json(HttpStatusCode.OK, """{"processed":0,"skipped":0,"errors":[{"index":0,"message":"boom"}]}"""));
        WriteContributions(Record("/manga/a", "A"));
        var uploader = new ContributionUploader(
            new SettingsService(EmptyConfig(), null!, null!),
            new ContributionUploadClient(new FakeFactory(handler), new CapturingLogger<ContributionUploadClient>(logs)),
            new JsonContributionUploadStateStore(Path.Combine(_folder, "upload-state.json")),
            ContributionsPath, new CapturingLogger<ContributionUploader>(logs), FastBackoff);

        await uploader.RunAsync();
        var failing = new FakeHandler(new HttpRequestException("connection refused"));
        var uploader2 = new ContributionUploader(
            new SettingsService(EmptyConfig(), null!, null!),
            new ContributionUploadClient(new FakeFactory(failing), new CapturingLogger<ContributionUploadClient>(logs)),
            new JsonContributionUploadStateStore(Path.Combine(_folder, "upload-state2.json")),
            ContributionsPath, new CapturingLogger<ContributionUploader>(logs), FastBackoff);
        await uploader2.RunAsync();

        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains(Uuid, StringComparison.OrdinalIgnoreCase));
    }

    // --- Settings: sentinel + persistence round-trip ---

    [Fact]
    public void UuidPolicy_SentinelKeepsStoredValue()
    {
        var incoming = new EditableSettingsDto { ContributionContributorUuid = SettingsService.UuidSentinel };
        SettingsService.ApplyContributorUuidPolicy(incoming, new EditableSettingsDto { ContributionContributorUuid = Uuid });
        Assert.Equal(Uuid, incoming.ContributionContributorUuid);
    }

    [Fact]
    public void UuidPolicy_NewValueReplaces_EmptyClears()
    {
        string replacement = "99999999-8888-7777-6666-555555555555";
        var incoming = new EditableSettingsDto { ContributionContributorUuid = replacement };
        SettingsService.ApplyContributorUuidPolicy(incoming, new EditableSettingsDto { ContributionContributorUuid = Uuid });
        Assert.Equal(replacement, incoming.ContributionContributorUuid);

        var clearing = new EditableSettingsDto { ContributionContributorUuid = "" };
        SettingsService.ApplyContributorUuidPolicy(clearing, new EditableSettingsDto { ContributionContributorUuid = Uuid });
        Assert.Equal("", clearing.ContributionContributorUuid);
    }

    [Fact]
    public void UuidPolicy_MalformedValueIsRejected()
    {
        var incoming = new EditableSettingsDto { ContributionContributorUuid = "definitely-not-a-guid" };
        Assert.Throws<ArgumentException>(() =>
            SettingsService.ApplyContributorUuidPolicy(incoming, new EditableSettingsDto()));
    }

    [Fact]
    public async Task MaskedSettings_ReplaceUuidWithSentinel_AndLeaveEmptyAlone()
    {
        using SettingsScope _ = SettingsScope.Enable();
        var service = new SettingsService(EmptyConfig(), null!, null!);
        SettingsDto masked = await service.GetMaskedSettingsAsync();
        Assert.Equal(SettingsService.UuidSentinel, masked.ContributionContributorUuid);
        // The cached instance itself must not be mutated by masking.
        Assert.Equal(Uuid, (await service.GetSettingsAsync()).ContributionContributorUuid);

        using SettingsScope noUuid = SettingsScope.Enable();
        noUuid.Set(s => s.ContributionContributorUuid = "");
        SettingsDto emptyMasked = await new SettingsService(EmptyConfig(), null!, null!).GetMaskedSettingsAsync();
        Assert.Equal("", emptyMasked.ContributionContributorUuid);
    }

    [Fact]
    public void UploadSettings_PersistenceRoundTrips()
    {
        var original = new EditableSettingsDto
        {
            ContributionUploadEnabled = true,
            ContributionContributorUuid = Uuid,
            ContributionUploadUrl = "https://contribution.rensaio.net"
        };
        List<RensaioBackend.Models.Database.SettingEntity> persisted = (List<RensaioBackend.Models.Database.SettingEntity>)
            typeof(SettingsService).GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [original])!;
        var restoredTuple = ((bool, EditableSettingsDto))typeof(SettingsService)
            .GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [persisted, new EditableSettingsDto()])!;

        Assert.True(restoredTuple.Item2.ContributionUploadEnabled);
        Assert.Equal(Uuid, restoredTuple.Item2.ContributionContributorUuid);
        Assert.Equal("https://contribution.rensaio.net", restoredTuple.Item2.ContributionUploadUrl);
    }

    // --- Helpers ---

    private string ContributionsPath => Path.Combine(_folder, "contributions-v1.json");

    private ContributionUploader NewUploader(FakeHandler handler, IContributionUploadStateStore? store = null)
        => new(
            new SettingsService(EmptyConfig(), null!, null!),
            new ContributionUploadClient(new FakeFactory(handler), NullLogger<ContributionUploadClient>.Instance),
            store ?? new JsonContributionUploadStateStore(Path.Combine(_folder, "upload-state.json")),
            ContributionsPath,
            NullLogger<ContributionUploader>.Instance,
            FastBackoff);

    private void WriteContributions(params ContributionRecordV1[] records)
    {
        Directory.CreateDirectory(_folder);
        var batch = new ContributionBatchV1 { GeneratedUtc = DateTime.UtcNow, Records = records.ToList() };
        File.WriteAllText(ContributionsPath,
            JsonSerializer.Serialize(batch, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static ContributionRecordV1 Record(string url, string title, string? internalId = null, long sourceId = 42) => new()
    {
        Id = internalId ?? ContributionIdentity.Create("pkg.example", sourceId, url),
        Package = "pkg.example",
        SourceId = sourceId,
        SourceName = "Example",
        SourceLanguage = "en",
        Url = url,
        Title = title,
        SeenInPopular = true
    };

    private static ContributionBlobPayloadV1 Payload(string url, string title,
        string? thumbnail = null, DateTime? observedUtc = null) => new()
    {
        Id = "internal-" + url,
        Package = "pkg.example",
        SourceId = 42,
        SourceName = "Example",
        SourceLanguage = "en",
        Url = url,
        Title = title,
        ThumbnailUrl = thumbnail,
        ObservedUtc = observedUtc ?? new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc)
    };

    private static int ItemCount(string requestBody)
    {
        using JsonDocument document = JsonDocument.Parse(requestBody);
        return document.RootElement.GetProperty("items").GetArrayLength();
    }

    private static (HttpStatusCode Status, string Body) ContributorOk()
        => (HttpStatusCode.OK, """{"active":true,"admin":true,"ban_reason":null}""");

    private static (HttpStatusCode Status, string Body) UploadOk()
        => (HttpStatusCode.OK, """{"processed":50,"skipped":0,"errors":[]}""");

    private static (HttpStatusCode Status, string Body) Json(HttpStatusCode status, string body) => (status, body);

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    /// <summary>
    /// Seeds SettingsService's static settings cache (the collector tests' established
    /// pattern) with upload enabled and restores the previous value on dispose.
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
            ContributionUploadEnabled = true,
            ContributionContributorUuid = Uuid,
            ContributionUploadUrl = ServerUrl
        });

        public void Set(Action<SettingsDto> mutate) => mutate(_current);

        public void Dispose() => Field.SetValue(null, _previous);
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new();
        public List<CapturedRequest> Requests { get; } = [];

        public FakeHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            foreach ((HttpStatusCode, string) response in responses)
                _responses.Enqueue(response);
        }

        public FakeHandler(Exception failure)
        {
            _responses.Enqueue(failure);
        }

        public void Enqueue((HttpStatusCode Status, string Body) response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(token);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            if (_responses.Count == 0)
                throw new InvalidOperationException("FakeHandler received more requests than scripted responses.");
            object next = _responses.Dequeue();
            if (next is Exception failure)
            {
                // Keep re-throwing the same failure for retries.
                _responses.Enqueue(failure);
                throw failure;
            }
            (HttpStatusCode status, string payload) = ((HttpStatusCode, string))next;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly FakeHandler _handler;
        public FakeFactory(FakeHandler handler) => _handler = handler;
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
