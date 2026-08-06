using RensaioBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

/// <summary>
/// A search result coming from a source whose extension is NOT installed (discovery search).
/// Superset of <see cref="LinkedSeriesDto"/> so a selected result can flow through the normal
/// augment/add pipeline once the extension has been installed via the regular install flow.
/// </summary>
public class DiscoverySeriesDto : LinkedSeriesDto
{
    /// <summary>Always false for discovery results; marker for the UI "Not installed" badge.</summary>
    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    /// <summary>Package name of the extension that owns this source (used to install it).</summary>
    [JsonPropertyName("extensionPkg")]
    public string ExtensionPkg { get; set; } = string.Empty;

    /// <summary>Name of the online repository the extension comes from (used to install it).</summary>
    [JsonPropertyName("extensionRepoName")]
    public string? ExtensionRepoName { get; set; }

    /// <summary>Display name of the extension.</summary>
    [JsonPropertyName("extensionName")]
    public string ExtensionName { get; set; } = string.Empty;

    /// <summary>Number of chapters, filled by the background details augmentation.</summary>
    [JsonPropertyName("chapterCount")]
    public int? ChapterCount { get; set; }

    /// <summary>Series status, filled by the background details augmentation.</summary>
    [JsonPropertyName("seriesStatus")]
    public SeriesStatus? SeriesStatus { get; set; }

    /// <summary>True when this result was surfaced from the community contribution snapshot
    /// rather than a live source crawl.</summary>
    [JsonPropertyName("fromSnapshot")]
    public bool FromSnapshot { get; set; }
}

/// <summary>
/// Counts of not-installed extensions/sources currently eligible for a discovery search,
/// so the UI can label the affordance "Search N more sources".
/// </summary>
public class DiscoverySourcesDto
{
    [JsonPropertyName("extensionCount")]
    public int ExtensionCount { get; set; }

    [JsonPropertyName("sourceCount")]
    public int SourceCount { get; set; }
}

/// <summary>Request body for starting (or attaching to) a streaming discovery sweep.</summary>
public class DiscoveryStartRequestDto
{
    [JsonPropertyName("keyword")]
    public string Keyword { get; set; } = string.Empty;

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }
}

/// <summary>
/// Response of the discovery start endpoint. When <see cref="Done"/> is true the sweep already
/// finished (cache hit / feature disabled / nothing eligible) and <see cref="Results"/> is the
/// complete set; otherwise the client should keep <see cref="SearchId"/> and listen for
/// "DiscoverySearch" SignalR events carrying incremental batches.
/// </summary>
public class DiscoveryStartDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("searchId")]
    public string? SearchId { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("totalExtensions")]
    public int TotalExtensions { get; set; }

    [JsonPropertyName("totalSources")]
    public int TotalSources { get; set; }

    [JsonPropertyName("completedExtensions")]
    public int CompletedExtensions { get; set; }

    /// <summary>Results so far (attach) or the full set (done).</summary>
    [JsonPropertyName("results")]
    public List<DiscoverySeriesDto> Results { get; set; } = [];
}

/// <summary>
/// SignalR event payload streamed on the shared /progress hub as "DiscoverySearch" while a
/// discovery sweep runs. Types: results, progress, completed, cancelled, failed.
/// </summary>
public class DiscoverySearchEventDto
{
    [JsonPropertyName("searchId")]
    public string SearchId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("completedExtensions")]
    public int CompletedExtensions { get; set; }

    [JsonPropertyName("totalExtensions")]
    public int TotalExtensions { get; set; }

    [JsonPropertyName("results")]
    public List<DiscoverySeriesDto>? Results { get; set; }

    [JsonPropertyName("totalResults")]
    public int? TotalResults { get; set; }
}
