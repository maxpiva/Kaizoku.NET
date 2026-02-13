using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

/// <summary>
/// Shared provider metadata used across multiple projections.
/// </summary>
public abstract class ProviderSummaryBase : IThumb
{
    [JsonPropertyName("provider")]
    public virtual string Provider { get; set; } = string.Empty;

    [JsonPropertyName("scanlator")]
    public virtual string Scanlator { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public virtual string Language { get; set; } = string.Empty;

    [JsonPropertyName("isStorage")]
    public virtual bool IsStorage { get; set; } = false;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public virtual string Title { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public virtual string? ThumbnailUrl { get; set; } = null;

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public virtual SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public virtual string? Url { get; set; } = null;
}
