using KaizokuBackend.Models.Abstractions;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

/// <summary>
/// Shared download metadata for queue and UI projections.
/// </summary>
public abstract class DownloadSummaryBase : IThumb
{
    [JsonPropertyName("title")]
    public virtual string Title { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public virtual string Provider { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public virtual string Language { get; set; } = string.Empty;

    [JsonPropertyName("scanlator")]
    public virtual string? Scanlator { get; set; }
        = null;

    [JsonPropertyName("thumbnailUrl")]
    public virtual string? ThumbnailUrl { get; set; }
        = null;

    [JsonPropertyName("url")]
    public virtual string? Url { get; set; }
        = null;
}
