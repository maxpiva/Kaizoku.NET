namespace KaizokuBackend.Models;

public class ProviderArchiveSnapshot : ChapterDescriptorBase
{
    public required string ArchiveName { get; set; }
    public DateTime? CreationDate { get; set; }
}
