using System.Diagnostics;
using System.Text.Json;
using Mihon.ExtensionsBridge.Models.Extensions;
using RensaioBackend.Services.Contributions.Snapshot;
using RensaioBackend.Services.Contributions.Upload;
using RensaioBackend.Services.Scrobbling;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// In-memory, lazily-reloaded search index over the decoded community contribution snapshot
/// (<c>snapshot-v1.json</c>). Milestone 3 writes that file atomically (temp + File.Move), so a
/// reader here never observes a partial file. This class turns the snapshot into a title/author
/// token index for fast keyword lookup that feeds discovery search.
///
/// No credentials are involved: the snapshot file on disk and the worker's <c>/key</c> endpoint
/// that produced it are public data. Nothing here reads settings, the contributor UUID, or any
/// machine-identifying value, and none of it is secret.
///
/// Thread-safety: every mutation happens under <see cref="_gate"/>; the served index is an
/// immutable <see cref="LoadedIndex"/> object swapped atomically, so <see cref="Search"/> readers
/// snapshot the current instance under the lock and then work lock-free against immutable state.
/// </summary>
public sealed class SnapshotSearchIndex
{
    /// <summary>Minimum fuzzy score (out of 100) for a scored candidate to survive.</summary>
    public const int MinScore = 55;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // A default JsonElement has ValueKind == Undefined, which throws when ModelExtensions
    // .FillBridgeItemInfo serializes the Manga. A parsed JSON "null" is a valid Null element that
    // serializes cleanly, so every ParsedManga we produce carries this shared, immutable element.
    private static readonly JsonElement NullMemo = JsonDocument.Parse("null").RootElement;

    private readonly string _snapshotFilePath;
    private readonly ILogger<SnapshotSearchIndex> _logger;
    private readonly TimeSpan _statInterval;
    private readonly object _gate = new();

    private LoadedIndex _index = LoadedIndex.Empty;
    private long _lastStatTicks = long.MinValue;

    /// <summary>DI constructor: derives the snapshot path the same way the downloader does.</summary>
    public SnapshotSearchIndex(IConfiguration configuration, ILogger<SnapshotSearchIndex> logger)
        : this(
            Path.Combine(configuration["runtimeDirectory"] ?? ".", "contributions", "snapshot", "snapshot-v1.json"),
            logger)
    {
    }

    /// <summary>
    /// Test / explicit-path constructor. <paramref name="statInterval"/> defaults to 30s; pass
    /// <see cref="TimeSpan.Zero"/> to force a stat check on every access.
    /// </summary>
    public SnapshotSearchIndex(string snapshotFilePath, ILogger<SnapshotSearchIndex> logger, TimeSpan? statInterval = null)
    {
        _snapshotFilePath = snapshotFilePath;
        _logger = logger;
        _statInterval = statInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>True when the loaded index holds at least one record.</summary>
    public bool HasRecords
    {
        get
        {
            EnsureFresh();
            return _index.Entries.Count > 0;
        }
    }

    /// <summary><see cref="File.GetLastWriteTimeUtc"/> ticks of the loaded file (0 when none), for cache keys.</summary>
    public long Stamp
    {
        get
        {
            EnsureFresh();
            return _index.WriteTicks;
        }
    }

    /// <summary>
    /// Searches the snapshot for <paramref name="keyword"/>, honouring the language filter, and
    /// returns up to <paramref name="maxResults"/> hits ordered best-score-first.
    /// </summary>
    public IReadOnlyList<SnapshotSearchHit> Search(string keyword, HashSet<string> languageSet, int maxResults = 100)
    {
        EnsureFresh();
        LoadedIndex index = _index;
        if (index.Entries.Count == 0 || string.IsNullOrWhiteSpace(keyword))
            return Array.Empty<SnapshotSearchHit>();

        // (2) Normalize + tokenize the keyword.
        string[] queryTokens = Tokenize(TitleMatcher.NormalizeForIndex(keyword));
        if (queryTokens.Length == 0)
            return Array.Empty<SnapshotSearchHit>();

        // (1)+(2) Candidate selection. An entry is a candidate when it passes the language filter
        // AND every query token prefix-matches at least one of its indexed tokens (AND semantics).
        // If AND yields nothing, fall back to OR (any query token prefix-matches). We track whether
        // a candidate was reached only via an author-token match so its score can be floored later.
        var candidates = new Dictionary<int, bool>(); // entry index -> matchedTitleOnly? (false => author-only)
        bool anyAndTitle = SelectCandidates(index, queryTokens, languageSet, requireAll: true, candidates);
        if (candidates.Count == 0)
            SelectCandidates(index, queryTokens, languageSet, requireAll: false, candidates);
        _ = anyAndTitle;
        if (candidates.Count == 0)
            return Array.Empty<SnapshotSearchHit>();

        // (3) Score. Title-scored candidates go through TitleMatcher against CloudTitle ?? Title.
        // Author-only matches are scored separately and floored at MinScore so a weak title score
        // cannot kill a legitimate author hit.
        var titleCandidates = new List<(string SearchTitle, int Id)>();
        var authorOnly = new List<int>();
        foreach ((int entryIndex, bool matchedTitle) in candidates)
        {
            if (matchedTitle)
            {
                SnapshotEntry entry = index.Entries[entryIndex];
                titleCandidates.Add((entry.CloudTitle ?? entry.Title, entryIndex));
            }
            else
            {
                authorOnly.Add(entryIndex);
            }
        }

        var scoreByEntry = new Dictionary<int, int>();
        if (titleCandidates.Count > 0)
        {
            var scored = TitleMatcher.MatchTitles(
                originalTitles: new[] { keyword },
                candidates: titleCandidates,
                minimumScore: MinScore);
            foreach ((_, int entryIndex, int percentage) in scored)
                scoreByEntry[entryIndex] = percentage;
        }
        foreach (int entryIndex in authorOnly)
        {
            // Author hit: keep it at the floor unless the title also scored higher above.
            if (!scoreByEntry.TryGetValue(entryIndex, out int existing) || existing < MinScore)
                scoreByEntry[entryIndex] = MinScore;
        }

        if (scoreByEntry.Count == 0)
            return Array.Empty<SnapshotSearchHit>();

        // (4) Order best score first, cap, and stamp the score onto each hit.
        return scoreByEntry
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => index.Entries[kv.Key].CloudTitle ?? index.Entries[kv.Key].Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(kv => index.Entries[kv.Key].ToHit(kv.Value))
            .ToArray();
    }

    /// <summary>
    /// Marks every entry that passes the language filter and matches the query tokens (prefix
    /// semantics; AND when <paramref name="requireAll"/>, else OR). Returns true if any match was a
    /// title/CloudTitle token match (as opposed to author-only). Uses a sorted-token binary search
    /// per query token, so candidate scan is O(entries × queryTokens × log(tokensPerEntry)).
    /// </summary>
    private static bool SelectCandidates(LoadedIndex index, string[] queryTokens, HashSet<string> languageSet,
        bool requireAll, Dictionary<int, bool> candidates)
    {
        bool anyTitleMatch = false;
        for (int i = 0; i < index.Entries.Count; i++)
        {
            SnapshotEntry entry = index.Entries[i];
            if (!MatchesLanguage(entry.SourceLanguage, languageSet))
                continue;

            bool allMatched = true;
            bool anyMatched = false;
            bool anyTitleTokenForEntry = false;
            foreach (string query in queryTokens)
            {
                bool titleHit = PrefixExists(entry.TitleTokens, query);
                bool authorHit = !titleHit && PrefixExists(entry.AuthorTokens, query);
                if (titleHit || authorHit)
                {
                    anyMatched = true;
                    if (titleHit)
                        anyTitleTokenForEntry = true;
                }
                else
                {
                    allMatched = false;
                }
            }

            bool matched = requireAll ? allMatched : anyMatched;
            if (!matched)
                continue;

            // matchedTitle=true when at least one query token hit a title/CloudTitle token; only
            // author-token matches make it author-only. OR mode preserves an existing title flag.
            bool matchedTitle = anyTitleTokenForEntry;
            if (candidates.TryGetValue(i, out bool existing))
                candidates[i] = existing || matchedTitle;
            else
                candidates[i] = matchedTitle;
            if (matchedTitle)
                anyTitleMatch = true;
        }
        return anyTitleMatch;
    }

    /// <summary>
    /// True when any token in <paramref name="sortedTokens"/> starts with <paramref name="prefix"/>.
    /// Binary search for the prefix's lower bound, then check the token at that position.
    /// </summary>
    private static bool PrefixExists(string[] sortedTokens, string prefix)
    {
        int lo = 0, hi = sortedTokens.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (string.CompareOrdinal(sortedTokens[mid], prefix) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo < sortedTokens.Length && sortedTokens[lo].StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mirrors DiscoverySearchService.MatchesLanguage: "all" always passes (ordinal, case-insensitive);
    /// otherwise the set (InvariantCultureIgnoreCase) must contain the language.
    /// </summary>
    private static bool MatchesLanguage(string? language, HashSet<string> languageSet)
        => !string.IsNullOrEmpty(language)
           && (string.Equals(language, "all", StringComparison.OrdinalIgnoreCase) || languageSet.Contains(language));

    private static string[] Tokenize(string normalized)
        => normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToArray();

    /// <summary>
    /// Lazy stat/reload gate. If <see cref="_statInterval"/> has elapsed since the last stat, compare
    /// the file's last-write time + length against the loaded generation and rebuild under the lock
    /// (double-checked) when they differ. No FileSystemWatcher, no hooks.
    /// </summary>
    private void EnsureFresh()
    {
        long now = Stopwatch.GetTimestamp();
        long intervalTicks = _statInterval.Ticks == 0
            ? 0
            : (long)(_statInterval.TotalSeconds * Stopwatch.Frequency);

        // Cheap read of the last-stat marker; TimeSpan.Zero forces a stat every call.
        if (intervalTicks != 0)
        {
            long last = Interlocked.Read(ref _lastStatTicks);
            if (last != long.MinValue && now - last < intervalTicks)
                return;
        }

        lock (_gate)
        {
            // Double-check inside the lock so a concurrent caller that just refreshed wins.
            if (intervalTicks != 0)
            {
                long last = _lastStatTicks;
                if (last != long.MinValue && now - last < intervalTicks)
                    return;
            }
            _lastStatTicks = now;

            long writeTicks;
            long length;
            try
            {
                var info = new FileInfo(_snapshotFilePath);
                if (!info.Exists)
                {
                    // Missing file → empty index. Only reset if we were holding something.
                    if (_index.Entries.Count > 0 || _index.WriteTicks != 0)
                        _index = LoadedIndex.Empty;
                    return;
                }
                writeTicks = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contribution snapshot search index could not stat '{Path}'; keeping the previous index.", _snapshotFilePath);
                return;
            }

            if (writeTicks == _index.WriteTicks && length == _index.Length)
                return; // Unchanged generation.

            LoadedIndex? rebuilt = TryBuild(writeTicks, length);
            if (rebuilt != null)
                _index = rebuilt;
            // On failure TryBuild already logged and we keep the previously loaded index.
        }
    }

    /// <summary>
    /// Deserializes the snapshot (Web defaults) and builds the immutable index. Returns null on any
    /// read/parse failure (logged) so the caller keeps the previous index; never throws to callers.
    /// </summary>
    private LoadedIndex? TryBuild(long writeTicks, long length)
    {
        ContributionSnapshotV1? snapshot;
        try
        {
            using FileStream stream = new(_snapshotFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            snapshot = JsonSerializer.Deserialize<ContributionSnapshotV1>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Contribution snapshot search index failed to read/parse '{Path}'; keeping the previous index.", _snapshotFilePath);
            return null;
        }
        if (snapshot is null)
        {
            _logger.LogWarning("Contribution snapshot search index deserialized to null for '{Path}'; keeping the previous index.", _snapshotFilePath);
            return null;
        }

        long start = Stopwatch.GetTimestamp();

        // titleId -> title, for resolving CloudTitle.
        var titleById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ContributionSnapshotTitleV1 title in snapshot.Titles)
            titleById[title.Id] = title.Title;

        // Dedupe records by key (last wins).
        var byKey = new Dictionary<string, ContributionSnapshotRecordV1>(StringComparer.Ordinal);
        foreach (ContributionSnapshotRecordV1 record in snapshot.Records)
            byKey[record.Key] = record;

        var entries = new List<SnapshotEntry>(byKey.Count);
        foreach (ContributionSnapshotRecordV1 record in byKey.Values)
        {
            ContributionBlobPayloadV1 payload = record.Payload;

            string? cloudTitle = null;
            if (!record.TitleIdDangling && titleById.TryGetValue(record.TitleId, out string? resolved))
                cloudTitle = resolved;

            // Inverted token index (tokens length >= 2) over the normalized title, CloudTitle and
            // author. Title/CloudTitle tokens are searchable-as-title; author tokens are separate
            // so author-only matches can be scored on their own.
            var titleTokens = new SortedSet<string>(StringComparer.Ordinal);
            AddTokens(titleTokens, payload.Title);
            AddTokens(titleTokens, cloudTitle);
            var authorTokens = new SortedSet<string>(StringComparer.Ordinal);
            AddTokens(authorTokens, payload.Author);

            entries.Add(new SnapshotEntry
            {
                Package = payload.Package,
                SourceId = payload.SourceId,
                SourceName = payload.SourceName,
                SourceLanguage = payload.SourceLanguage,
                Url = payload.Url,
                RealUrl = payload.RealUrl,
                Title = payload.Title,
                ThumbnailUrl = payload.ThumbnailUrl,
                Author = payload.Author,
                Artist = payload.Artist,
                Genre = payload.Genre,
                CloudTitle = cloudTitle,
                TitleIdDangling = record.TitleIdDangling,
                TitleTokens = titleTokens.ToArray(),
                AuthorTokens = authorTokens.ToArray()
            });
        }

        double buildMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        _logger.LogInformation("Contribution snapshot search index built: {Count} records in {Ms:F1} ms.", entries.Count, buildMs);
        return new LoadedIndex(entries, writeTicks, length);
    }

    private static void AddTokens(SortedSet<string> set, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        foreach (string token in TitleMatcher.NormalizeForIndex(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 2)
                set.Add(token);
        }
    }

    /// <summary>
    /// Maps a hit onto a <see cref="ParsedManga"/> the discovery pipeline can consume. Status is
    /// UNKNOWN (the unknown-equivalent), Initialized is false, and Memo is a parsed JSON "null" so
    /// FillBridgeItemInfo's serialize step does not throw on a default (Undefined) JsonElement.
    /// </summary>
    public static ParsedManga ToParsedManga(SnapshotSearchHit hit)
    {
        return new ParsedManga
        {
            Url = hit.Url,
            RealUrl = hit.RealUrl ?? string.Empty,
            Title = hit.Title,
            ThumbnailUrl = hit.ThumbnailUrl,
            Author = hit.Author,
            Artist = hit.Artist,
            Genre = hit.Genre,
            Initialized = false,
            Status = Mihon.ExtensionsBridge.Models.Extensions.Status.UNKNOWN,
            Memo = NullMemo
        };
    }

    /// <summary>Immutable, atomically-swapped index generation.</summary>
    private sealed class LoadedIndex
    {
        public static readonly LoadedIndex Empty = new(new List<SnapshotEntry>(), 0, 0);

        public IReadOnlyList<SnapshotEntry> Entries { get; }
        public long WriteTicks { get; }
        public long Length { get; }

        public LoadedIndex(IReadOnlyList<SnapshotEntry> entries, long writeTicks, long length)
        {
            Entries = entries;
            WriteTicks = writeTicks;
            Length = length;
        }
    }

    /// <summary>One indexed record: the served hit fields plus the sorted token arrays.</summary>
    private sealed class SnapshotEntry
    {
        public string Package { get; init; } = string.Empty;
        public long SourceId { get; init; }
        public string SourceName { get; init; } = string.Empty;
        public string SourceLanguage { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string? RealUrl { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? ThumbnailUrl { get; init; }
        public string? Author { get; init; }
        public string? Artist { get; init; }
        public string? Genre { get; init; }
        public string? CloudTitle { get; init; }
        public bool TitleIdDangling { get; init; }
        public string[] TitleTokens { get; init; } = Array.Empty<string>();
        public string[] AuthorTokens { get; init; } = Array.Empty<string>();

        public SnapshotSearchHit ToHit(int score) => new()
        {
            Package = Package,
            SourceId = SourceId,
            SourceName = SourceName,
            SourceLanguage = SourceLanguage,
            Url = Url,
            RealUrl = RealUrl,
            Title = Title,
            ThumbnailUrl = ThumbnailUrl,
            Author = Author,
            Artist = Artist,
            Genre = Genre,
            CloudTitle = CloudTitle,
            TitleIdDangling = TitleIdDangling,
            Score = score
        };
    }
}

/// <summary>One search result from the snapshot index (description dropped to save memory).</summary>
public sealed class SnapshotSearchHit
{
    public string Package { get; init; } = string.Empty;
    public long SourceId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? RealUrl { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public string? Author { get; init; }
    public string? Artist { get; init; }
    public string? Genre { get; init; }
    public string? CloudTitle { get; init; }
    public bool TitleIdDangling { get; init; }
    public int Score { get; init; }
}
