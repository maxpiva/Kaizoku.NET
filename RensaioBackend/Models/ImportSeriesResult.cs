using RensaioBackend.Models.Enums;
using RensaioBackend.Models.ReadState;
using System;
using System.Text.Json.Serialization;

namespace RensaioBackend.Models;

/// <summary>
/// Shared series metadata payload for import and archive projections.
/// </summary>
public class ImportSeriesResult
{
    private List<ImportProviderSnapshot> _providers = new();

    public string Title { get; set; } = string.Empty;
    public Guid? InstanceId { get; set; }
    public string? DisplayName { get; set; }
    public decimal? StartChapter { get; set; }
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public string Artist { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Genre { get; set; } = [];
    public string Type { get; set; } = string.Empty;
    public int ChapterCount { get; set; }
    public DateTime? LastUpdatedUTC { get; set; }
    public bool IsDisabled { get; set; }

    public int Version { get; set; } = 1;

    [JsonPropertyName("KaizokuVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public int KaizokuVersion
    {
        get => 0;
        set => Version = value;
    }

    public List<UserReadStateSnapshot>? UserReadStates { get; set; }

    /// <summary>
    /// Cached external mappings from SeriesMappings table.
    /// Maps ExternalSeriesProvider enum name (e.g., "MyAnimeList") to ExternalSeriesId.
    /// Serialized into rensaio.json for offline/cached automatch fallback.
    /// </summary>
    public List<ExternalMapping>? ExternalMappings { get; set; }

    public List<ImportProviderSnapshot> Providers
    {
        get => _providers;
        set => _providers = value ?? [];
    }
}
public class ExternalMapping
{
    public string Provider { get; set; }
    public string ExternalId { get; set; }
    public string ExternalTitle { get; set; }
}
