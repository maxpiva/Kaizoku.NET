using KaizokuBackend.Models.Abstractions;

namespace KaizokuBackend.Migration.Models
{
    public class SuwayomiChapter : IChapterIndex
    {
        public int Id { get; set; }
        public string Url { get; set; } = "";
        public string Name { get; set; } = "";
        public long UploadDate { get; set; }
        public decimal? ChapterNumber { get; set; }
        public string? Scanlator { get; set; }
        public int MangaId { get; set; }
        public bool Read { get; set; }
        public bool Bookmarked { get; set; }
        public long LastPageRead { get; set; }
        public long LastReadAt { get; set; }
        public int Index { get; set; }
        public long FetchedAt { get; set; }
        public string RealUrl { get; set; } = "";
        public bool Downloaded { get; set; }
        public int PageCount { get; set; }
        public int ChapterCount { get; set; }
        public Dictionary<string, string> Meta { get; set; } = new();
    }
}
