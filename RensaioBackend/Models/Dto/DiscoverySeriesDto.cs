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
