using KaizokuBackend.Models.Abstractions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Database;
[Index(nameof(MihonProviderId))]
[Index(nameof(MihonId))]
[Index(nameof(SeriesId))]
[Index(nameof(Title), nameof(Language))]
[Index(nameof(Provider), nameof(Language), nameof(Scanlator))]
public class SeriesProviderEntity : ProviderSummaryBase, IBridgeItemInfo, IThumb
{
    [Key]
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public string? MihonProviderId { get; set; }
    public string? MihonId { get; set; }
    public string? BridgeItemInfo { get; set; }
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
    public bool IsLocal { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsUninstalled { get; set; }
    public List<Chapter> Chapters { get; set; } = [];

}
