using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// stdin/stdout contract between the backend and a discovery-search worker process
/// (spawned as <c>RensaioBackend --discovery-worker</c>). The parent writes one
/// <see cref="DiscoveryWorkerInput"/> JSON document to the worker's stdin; the worker streams
/// <see cref="DiscoveryWorkerEvent"/> JSON lines on stdout (logs go to stderr only).
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

public class DiscoveryWorkerInput
{
    /// <summary>Worker-private scratch root; the worker creates android/ and temp/ under it. Never the parent's bridge folder.</summary>
    public string ScratchFolder { get; set; } = string.Empty;
    /// <summary>Parent's bridge preferences (FlareSolverr, SOCKS, interceptors) to apply before searching.</summary>
    public Preferences? Preferences { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = [];
    public double SearchTimeoutSeconds { get; set; } = 120;
    /// <summary>How many extensions the worker may classload+search concurrently.</summary>
    public int MaxParallelExtensions { get; set; } = 1;
    public List<DiscoveryWorkerExtension> Extensions { get; set; } = [];
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
    /// <summary>Whole batch finished; the worker exits right after emitting this.</summary>
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
    public string? Error { get; set; }
}
