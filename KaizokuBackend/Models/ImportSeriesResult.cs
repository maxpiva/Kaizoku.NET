using System;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models;

/// <summary>
/// Shared series metadata payload for import and archive projections.
/// </summary>
public class ImportSeriesResult
{
    private List<ImportProviderSnapshot> _providers = new();

    public string Title { get; set; } = string.Empty;
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public string Artist { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Genre { get; set; } = [];
    public string Type { get; set; } = string.Empty;
    public int ChapterCount { get; set; }
    public DateTime? LastUpdatedUTC { get; set; }
    public bool IsDisabled { get; set; }
    public int KaizokuVersion { get; set; } = 1;

    public List<ImportProviderSnapshot> Providers
    {
        get => _providers;
        set => _providers = value ?? [];
    }
}
