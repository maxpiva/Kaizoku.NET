using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// stdin/stdout contract between the backend and a discovery-search worker process
/// (spawned as <c>RensaioBackend --discovery-worker</c>).
///
/// The parent writes ONE <see cref="DiscoveryWorkerInit"/> JSON line to the worker's stdin,
/// followed by any number of <see cref="DiscoveryWorkerRequest"/> JSON lines (the worker stays
/// resident between requests, keeping its classloaded extensions warm). The worker streams
/// <see cref="DiscoveryWorkerEvent"/> JSON lines on stdout — every protocol line prefixed with
/// <see cref="DiscoveryWorkerJson.LinePrefix"/> — ending each request with a done event.
/// Closing stdin (or an exit request) shuts the worker down. Logs go to stderr only.
/// </summary>
public static class DiscoveryWorkerJson
{
    /// <summary>
    /// Marker prefixed to every protocol line the worker writes on stdout. Extension/OkHttp/
    /// android-compat code can print via Java System.out (IKVM routes it to fd 1) — the worker
    /// redirects those to stderr, but as a second line of defense the parent only parses lines
    /// bearing this prefix; anything else on stdout is treated as stray output and ignored.
    /// </summary>
    public const string LinePrefix = "@RSW@";

    /// <summary>
    /// IncludeFields because <see cref="Preferences.Interceptors"/> is a public field, not a property.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>First stdin line: one-time worker bootstrap.</summary>
public class DiscoveryWorkerInit
{
    /// <summary>Worker-private scratch root; the worker creates android/ and temp/ under it. Never the parent's bridge folder.</summary>
    public string ScratchFolder { get; set; } = string.Empty;
    /// <summary>Parent's bridge preferences (FlareSolverr, SOCKS, interceptors) to apply before any request.</summary>
    public Preferences? Preferences { get; set; }
}

public static class DiscoveryWorkerRequestTypes
{
    /// <summary>Classload (if needed) and search every extension in the request.</summary>
    public const string Search = "search";
    /// <summary>Fetch details+chapters for one manga of one source (chapter count / status).</summary>
    public const string Details = "details";
    /// <summary>Polite shutdown; equivalent to closing stdin.</summary>
    public const string Exit = "exit";
}

/// <summary>One unit of work; the worker answers with events and a final done event.</summary>
public class DiscoveryWorkerRequest
{
    public string Type { get; set; } = DiscoveryWorkerRequestTypes.Search;
    public string Query { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = [];
    public double SearchTimeoutSeconds { get; set; } = 120;
    /// <summary>How many extensions the worker may classload+search concurrently (search requests).</summary>
    public int MaxParallelExtensions { get; set; } = 1;
    /// <summary>Extensions to ensure loaded. For details requests: exactly one (the manga's owner).</summary>
    public List<DiscoveryWorkerExtension> Extensions { get; set; } = [];
    /// <summary>Details requests: id of the source within the extension.</summary>
    public long? SourceId { get; set; }
    /// <summary>Details requests: the serialized bridge Manga (LinkedSeries BridgeItemInfo).</summary>
    public string? MangaJson { get; set; }
}

public class DiscoveryWorkerExtension
{
    /// <summary>Manifest-derived metadata from the parent's prepare step (jar file name, class name, package...).</summary>
    public RepositoryEntry Entry { get; set; } = null!;
    /// <summary>Absolute path of the discovery cache folder holding the converted JAR.</summary>
    public string Folder { get; set; } = string.Empty;
}

public static class DiscoveryWorkerEventTypes
{
    /// <summary>Extension classload is about to start. If the worker dies before the matching
    /// extensionDone/extensionFailed, the parent treats this extension as the crash suspect.</summary>
    public const string Begin = "begin";
    public const string SourceResult = "sourceResult";
    public const string ExtensionDone = "extensionDone";
    /// <summary>Managed (non-fatal) failure loading or searching an extension; the worker continues.</summary>
    public const string ExtensionFailed = "extensionFailed";
    /// <summary>Answer to a details request (chapter count + status, or Error).</summary>
    public const string Details = "details";
    /// <summary>Current request finished; the worker awaits the next request (warm) on stdin.</summary>
    public const string Done = "done";
}

public class DiscoveryWorkerEvent
{
    public string Type { get; set; } = string.Empty;
    public string? Package { get; set; }
    public long? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceLanguage { get; set; }
    public List<ParsedManga>? Mangas { get; set; }
    /// <summary>sourceResult: the headers the source's own HTTP client sends for image requests.</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>details: number of chapters found for the manga.</summary>
    public int? ChapterCount { get; set; }
    /// <summary>details: the manga's status (bridge Status enum value).</summary>
    public int? MangaStatus { get; set; }
    public string? Error { get; set; }
}
