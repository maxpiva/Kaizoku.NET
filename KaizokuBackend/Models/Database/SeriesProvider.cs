using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Models.Database;

public class SeriesProvider
{
    [Key]
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public int SuwayomiId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Scanlator { get; set; } = "";
    public string? Url { get; set; }
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    public string? ThumbnailUrl { get; set; } = null;
    public string? Artist { get; set; } = null;
    public string? Author { get; set; } = null;
    public string? Description { get; set; } = null;
    public List<string> Genre { get; set; } = new();
    public DateTime? FetchDate { get; set; }
    public long? ChapterCount { get; set; } = null;
    public decimal? ContinueAfterChapter { get; set; }
    public bool IsTitle { get; set; }
    public bool IsCover { get; set; }
    public bool IsUnknown { get; set; }
    public bool IsStorage { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsUninstalled { get; set; }
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public List<Chapter> Chapters { get; set; } = [];

}

[Index(nameof(SuwayomiSourceId))]
[Index(nameof(FetchDate))]
[Index(nameof(Title))]
public class LatestSerie
{
    [Key]
    public int SuwayomiId { get; set; }
    public string SuwayomiSourceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Language { get; set; } = "";
    public string? Url { get; set; }
    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; } = null;
    public string? Artist { get; set; } = null;
    public string? Author { get; set; } = null;
    public string? Description { get; set; } = null;
    public List<string> Genre { get; set; } = new();
    public DateTime FetchDate { get; set; }
    public long? ChapterCount { get; set; } = null;
    public decimal? LatestChapter { get; set; }
    public string LatestChapterTitle { get; set; } = "";
    public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
    public InLibraryStatus InLibrary { get; set; } = InLibraryStatus.NotInLibrary;
    public Guid? SeriesId { get; set; } = null;
    public List<SuwayomiChapter> Chapters { get; set; } = [];
}

public enum InLibraryStatus
{
    NotInLibrary,
    InLibrary,
    InLibraryButDisabled
}
