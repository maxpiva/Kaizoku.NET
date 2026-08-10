using Microsoft.Extensions.Logging.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using RensaioBackend.Extensions;
using RensaioBackend.Models.Abstractions;
using RensaioBackend.Models.Database;
using RensaioBackend.Services.Contributions.Snapshot;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Search.Discovery;
using System.Text.Json;
using Xunit;

namespace RensaioBackend.Tests;

/// <summary>
/// Milestone 4 step 1: <see cref="SnapshotSearchIndex"/>, the in-memory, lazily-reloaded keyword
/// index built over the decoded community contribution snapshot (snapshot-v1.json). Mirrors
/// <see cref="ContributionSnapshotTests"/>'s scaffolding (temp folder per test, IDisposable
/// cleanup, NullLogger) but does NOT use SettingsScope: per the class doc comment on
/// <see cref="SnapshotSearchIndex"/>, it reads no settings by design.
///
/// Fixtures are written as real <see cref="ContributionSnapshotV1"/> objects (with real
/// <see cref="ContributionBlobPayloadV1"/> payloads) serialized with JsonSerializerDefaults.Web,
/// exactly how the milestone 3 downloader writes snapshot-v1.json.
/// </summary>
public sealed class SnapshotSearchIndexTests : IDisposable
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "rensaio-snapshotsearchindex-tests", Guid.NewGuid().ToString("N"));

    private string SnapshotPath => Path.Combine(_folder, "snapshot-v1.json");

    public SnapshotSearchIndexTests() => Directory.CreateDirectory(_folder);

    // --- 1. Missing file ---

    [Fact]
    public void Search_MissingFile_ReturnsEmpty_NoThrow()
    {
        string missingPath = Path.Combine(_folder, "does-not-exist.json");
        SnapshotSearchIndex index = NewIndex(missingPath);

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("berserk", Lang("en"));

        Assert.Empty(hits);
        Assert.False(index.HasRecords);
        Assert.Equal(0L, index.Stamp);
    }

    // --- 2. Malformed JSON ---

    [Fact]
    public void Search_MalformedJson_ReturnsEmpty_NoThrow()
    {
        File.WriteAllBytes(SnapshotPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF]);
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("berserk", Lang("en"));

        Assert.Empty(hits);
        Assert.False(index.HasRecords);
    }

    // --- 3. Exact title: full field round-trip ---

    [Fact]
    public void Search_ExactTitle_ReturnsHitWithMappedFields()
    {
        ContributionBlobPayloadV1 payload = Payload(
            title: "My Exact Title",
            package: "eu.kanade.tachiyomi.extension.en.mangadex",
            sourceId: 12345L,
            sourceName: "MangaDex",
            sourceLanguage: "en",
            url: "/manga/exact",
            realUrl: "https://mangadex.org/title/exact",
            thumbnailUrl: "https://example.com/thumb.jpg",
            author: "Jane Author",
            artist: "Jane Artist",
            genre: "Action, Adventure");
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "orphan-title-id", dangling: true, payload)));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("My Exact Title", Lang("en"));

        SnapshotSearchHit hit = Assert.Single(hits);
        Assert.Equal("eu.kanade.tachiyomi.extension.en.mangadex", hit.Package);
        Assert.Equal(12345L, hit.SourceId);
        Assert.Equal("MangaDex", hit.SourceName);
        Assert.Equal("en", hit.SourceLanguage);
        Assert.Equal("/manga/exact", hit.Url);
        Assert.Equal("https://mangadex.org/title/exact", hit.RealUrl);
        Assert.Equal("My Exact Title", hit.Title);
        Assert.Equal("https://example.com/thumb.jpg", hit.ThumbnailUrl);
        Assert.Equal("Jane Author", hit.Author);
        Assert.Equal("Jane Artist", hit.Artist);
        Assert.Equal("Action, Adventure", hit.Genre);
        Assert.Null(hit.CloudTitle);
        Assert.True(hit.TitleIdDangling);
        Assert.Equal(100, hit.Score);
    }

    // --- 4. Case / diacritic insensitivity ---

    [Fact]
    public void Search_IsCaseAndDiacriticInsensitive()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Berserk"))));
        SnapshotSearchIndex index = NewIndex();

        Assert.Single(index.Search("BERSERK", Lang("en")));
        Assert.Single(index.Search("bérserk", Lang("en")));
    }

    // --- 5. Author-only match, floored at MinScore ---

    [Fact]
    public void Search_MatchesAuthor()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "t1", dangling: true,
            Payload("Completely Unrelated Story", author: "Kentaro Miura"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("Kentaro Miura", Lang("en"));

        SnapshotSearchHit hit = Assert.Single(hits);
        Assert.Equal("Kentaro Miura", hit.Author);
        // Author-only matches never go through TitleMatcher; they are floored at exactly MinScore.
        Assert.Equal(SnapshotSearchIndex.MinScore, hit.Score);
    }

    // --- 6. Linked cloud title ---

    [Fact]
    public void Search_MatchesLinkedCloudTitle()
    {
        var titles = new List<ContributionSnapshotTitleV1> { new() { Id = "cloud-1", Title = "One Piece" } };
        WriteSnapshot(Snapshot(titles, MakeRecord("k1", "cloud-1", dangling: false, Payload("OP"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("one piece", Lang("en"));

        SnapshotSearchHit hit = Assert.Single(hits);
        Assert.Equal("One Piece", hit.CloudTitle);
        Assert.Equal("OP", hit.Title);
        Assert.False(hit.TitleIdDangling);
    }

    // --- 7. Dangling record still searchable by payload title ---

    [Fact]
    public void Search_DanglingRecord_StillSearchableByPayloadTitle()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "gone-id", dangling: true, Payload("Naruto Shippuden"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("naruto shippuden", Lang("en"));

        SnapshotSearchHit hit = Assert.Single(hits);
        Assert.True(hit.TitleIdDangling);
        Assert.Null(hit.CloudTitle);
    }

    // --- 8. Language filter; "all" always passes; language set is case-insensitive ---

    [Fact]
    public void Search_FiltersByLanguage_AllAlwaysPasses()
    {
        // Both records share the exact same normalized title (guarantees a deterministic
        // score-100 match for whichever one survives the language filter); they're
        // distinguished only by SourceLanguage/Url.
        WriteSnapshot(Snapshot(null,
            MakeRecord("k1", "t1", dangling: true, Payload("Berserk", sourceLanguage: "ja", url: "/manga/ja")),
            MakeRecord("k2", "t2", dangling: true, Payload("Berserk", sourceLanguage: "all", url: "/manga/all"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hitsEn = index.Search("berserk", Lang("en"));
        Assert.Single(hitsEn);
        Assert.Equal("/manga/all", hitsEn[0].Url);

        IReadOnlyList<SnapshotSearchHit> hitsUpperEn = index.Search("berserk", Lang("EN"));
        Assert.Single(hitsUpperEn);
        Assert.Equal("/manga/all", hitsUpperEn[0].Url);
    }

    // --- 9. Unrelated title below threshold ---

    [Fact]
    public void Search_UnrelatedTitle_BelowThreshold_Excluded()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Completely Different Story"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("berserk", Lang("en"));

        Assert.Empty(hits);
    }

    // --- 10. Cap results, best score first ---

    [Fact]
    public void Search_CapsResults_BestScoreFirst()
    {
        WriteSnapshot(Snapshot(null,
            MakeRecord("k1", "t1", dangling: true, Payload("Berserk")),
            MakeRecord("k2", "t2", dangling: true, Payload("Berserk Extra")),
            MakeRecord("k3", "t3", dangling: true, Payload("Berserks")),
            MakeRecord("k4", "t4", dangling: true, Payload("Berserker")),
            MakeRecord("k5", "t5", dangling: true, Payload("Berserkr")),
            MakeRecord("k6", "t6", dangling: true, Payload("Berserking"))));
        SnapshotSearchIndex index = NewIndex();

        IReadOnlyList<SnapshotSearchHit> hits = index.Search("berserk", Lang("en"), maxResults: 3);

        Assert.Equal(3, hits.Count);
        Assert.Equal("Berserk", hits[0].Title);
        Assert.Equal(100, hits[0].Score);
        for (int i = 1; i < hits.Count; i++)
            Assert.True(hits[i - 1].Score >= hits[i].Score);
    }

    // --- 11. AND prefilter falls back to OR ---

    [Fact]
    public void Search_AndPrefilter_FallsBackToOr()
    {
        WriteSnapshot(Snapshot(null,
            MakeRecord("k1", "t1", dangling: true, Payload("Solo Leveling")),
            MakeRecord("k2", "t2", dangling: true, Payload("Solo Camping"))));
        SnapshotSearchIndex index = NewIndex();

        // "solo leveling" AND-matches only the record whose tokens contain both "solo" and
        // "leveling" — Solo Camping is excluded even though it shares "solo".
        IReadOnlyList<SnapshotSearchHit> andHits = index.Search("solo leveling", Lang("en"));
        Assert.Single(andHits);
        Assert.Equal("Solo Leveling", andHits[0].Title);

        // "solo qq" has no entry matching both tokens, so AND yields nothing and the search
        // falls back to OR: any entry whose tokens contain "solo" comes back (both records score
        // above MinScore against the fuzzy matcher, verified empirically: Solo Camping:64,
        // Solo Leveling:63).
        IReadOnlyList<SnapshotSearchHit> orHits = index.Search("solo qq", Lang("en"));
        Assert.Equal(2, orHits.Count);
        Assert.Contains(orHits, h => h.Title == "Solo Leveling");
        Assert.Contains(orHits, h => h.Title == "Solo Camping");
    }

    // --- 12. Reload on new file ---

    [Fact]
    public void Reload_NewSnapshotFile_IsPickedUp()
    {
        // Titles chosen with no character/token overlap at all (verified empirically: fuzzy score
        // 0 against each other), so there's no risk of the old query still fuzzy-matching the new
        // record (or vice versa) once the file is swapped.
        WriteSnapshot(
            Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Aardvark Sunrise"))),
            new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc));
        SnapshotSearchIndex index = NewIndex();
        Assert.Single(index.Search("aardvark sunrise", Lang("en")));
        long stamp1 = index.Stamp;

        WriteSnapshot(
            Snapshot(null, MakeRecord("k2", "t2", dangling: true, Payload("Zebra Twilight"))),
            new DateTime(2026, 8, 6, 12, 0, 5, DateTimeKind.Utc));

        Assert.Single(index.Search("zebra twilight", Lang("en")));
        Assert.Empty(index.Search("aardvark sunrise", Lang("en")));
        Assert.NotEqual(stamp1, index.Stamp);
    }

    // --- 13. Corrupt overwrite keeps serving the old index ---

    [Fact]
    public void Reload_CorruptOverwrite_KeepsServingOldIndex()
    {
        WriteSnapshot(
            Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Good Title"))),
            new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc));
        SnapshotSearchIndex index = NewIndex();
        Assert.Single(index.Search("good title", Lang("en")));

        File.WriteAllBytes(SnapshotPath, [0x00, 0x01, 0x02, 0x03]);
        File.SetLastWriteTimeUtc(SnapshotPath, new DateTime(2026, 8, 6, 12, 0, 5, DateTimeKind.Utc));

        Assert.Single(index.Search("good title", Lang("en")));
        Assert.True(index.HasRecords);
    }

    // --- 14. Dedupe by record key, last wins ---

    [Fact]
    public void Index_DedupesByRecordKey_LastWins()
    {
        // Unrelated titles (no shared tokens, verified empirically: fuzzy score 9 against each
        // other, well under MinScore) so a query for the discarded title can't accidentally
        // fuzzy-match the surviving one.
        WriteSnapshot(Snapshot(null,
            MakeRecord("dup", "t1", dangling: true, Payload("Koala Melody")),
            MakeRecord("dup", "t2", dangling: true, Payload("Wombat Serenade"))));
        SnapshotSearchIndex index = NewIndex();

        Assert.Empty(index.Search("koala melody", Lang("en")));
        IReadOnlyList<SnapshotSearchHit> hits = index.Search("wombat serenade", Lang("en"));
        SnapshotSearchHit hit = Assert.Single(hits);
        Assert.Equal("Wombat Serenade", hit.Title);
    }

    // --- 15. ToParsedManga survives FillBridgeItemInfo / ToManga round trip ---

    [Fact]
    public void ToParsedManga_SurvivesFillBridgeItemInfo_RoundTrip()
    {
        WriteSnapshot(Snapshot(null,
            MakeRecord("k1", "t1", dangling: true, Payload("Round Trip Title", url: "/manga/round-trip"))));
        SnapshotSearchIndex index = NewIndex();
        SnapshotSearchHit hit = Assert.Single(index.Search("round trip title", Lang("en")));

        ParsedManga parsed = SnapshotSearchIndex.ToParsedManga(hit);
        IBridgeItemInfo target = new LatestSerieEntity { MihonId = "test-mihon-id" };

        // Guards the Memo/JsonElement pitfall: FillBridgeItemInfo JSON-serializes the Manga, and a
        // default (unset) JsonElement (ValueKind == Undefined) throws there. ToParsedManga must
        // hand back a hit whose Memo is the shared "null" JsonElement, not a default one.
        Exception? ex = Xunit.Record.Exception(() => parsed.FillBridgeItemInfo(target));
        Assert.Null(ex);

        Manga? roundTripped = target.ToManga();
        Assert.NotNull(roundTripped);
        Assert.Equal(hit.Url, roundTripped!.Url);
        Assert.Equal(hit.Title, roundTripped.Title);
    }

    // --- Additional: empty keyword ---

    [Fact]
    public void Search_EmptyOrWhitespaceKeyword_ReturnsEmpty()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Berserk"))));
        SnapshotSearchIndex index = NewIndex();

        Assert.Empty(index.Search("", Lang("en")));
        Assert.Empty(index.Search("   ", Lang("en")));
    }

    // --- Additional: sub-2-char tokens are dropped, leaving nothing to search ---

    [Fact]
    public void Search_TokensShorterThanTwoChars_AreIgnored()
    {
        WriteSnapshot(Snapshot(null, MakeRecord("k1", "t1", dangling: true, Payload("Berserk"))));
        SnapshotSearchIndex index = NewIndex();

        // Every token here is length 1, so Tokenize drops them all and the query becomes empty.
        Assert.Empty(index.Search("a b", Lang("en")));
    }

    // --- Helpers ---

    private SnapshotSearchIndex NewIndex(string? path = null)
        => new(path ?? SnapshotPath, NullLogger<SnapshotSearchIndex>.Instance, TimeSpan.Zero);

    private static HashSet<string> Lang(params string[] languages)
        => new(languages, StringComparer.InvariantCultureIgnoreCase);

    private void WriteSnapshot(ContributionSnapshotV1 snapshot, DateTime? writeTimeUtc = null)
    {
        File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(snapshot, WireOptions));
        File.SetLastWriteTimeUtc(SnapshotPath, writeTimeUtc ?? DateTime.UtcNow);
    }

    private static ContributionSnapshotV1 Snapshot(
        List<ContributionSnapshotTitleV1>? titles, params ContributionSnapshotRecordV1[] records)
        => new()
        {
            Version = 1,
            GeneratedUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc),
            Titles = titles ?? [],
            Records = records.ToList(),
            Metadata = []
        };

    private static ContributionSnapshotRecordV1 MakeRecord(
        string key, string titleId, bool dangling, ContributionBlobPayloadV1 payload)
        => new() { Key = key, TitleId = titleId, TitleIdDangling = dangling, Payload = payload };

    private static ContributionBlobPayloadV1 Payload(
        string title,
        string package = "pkg.example",
        long sourceId = 42,
        string sourceName = "Example Source",
        string sourceLanguage = "en",
        string url = "/manga/default",
        string? realUrl = null,
        string? thumbnailUrl = null,
        string? author = null,
        string? artist = null,
        string? genre = null)
        => new()
        {
            Package = package,
            SourceId = sourceId,
            SourceName = sourceName,
            SourceLanguage = sourceLanguage,
            Url = url,
            RealUrl = realUrl,
            Title = title,
            ThumbnailUrl = thumbnailUrl,
            Author = author,
            Artist = artist,
            Genre = genre,
            ObservedUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
        };

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }
}
