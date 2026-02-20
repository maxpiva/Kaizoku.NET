using KaizokuBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Migration.Models
{
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
}
