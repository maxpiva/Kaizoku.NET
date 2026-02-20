using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Models.Database;

[Index(nameof(MihonProviderId))]
[Index(nameof(FetchDate))]
[Index(nameof(Title))]
[Index(nameof(Provider))]
public class LatestSerieEntity : IBridgeItemInfo, IThumb
{
    [Key]
    public string MihonId { get; set; }
    public string? MihonProviderId { get; set; }
    public string? BridgeItemInfo { get; set; }
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
    public List<Mihon.ExtensionsBridge.Models.Extensions.ParsedChapter> Chapters { get; set; } = [];
}
