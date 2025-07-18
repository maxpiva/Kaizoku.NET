namespace KaizokuBackend.Models;

public class ArchiveInfo : IChapterIndex
{
    public required string ArchiveName { get; set; }
    public DateTime? CreationDate { get; set; }

    public decimal? ChapterNumber { get; set; }

    public int Index { get; set; }
}