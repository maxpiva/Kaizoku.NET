using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderInfo
{

    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public string Scanlator { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; } = "";
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public bool IsStorage { get; set; } = false;
    public int ChapterCount { get; set; }
    public List<StartStop> ChapterList { get; set; } = [];
    public bool IsDisabled { get; set; }
    public List<ArchiveInfo> Archives { get; set; } = [];

}