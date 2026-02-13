using KaizokuBackend.Models.Abstractions;

namespace KaizokuBackend.Models;

/// <summary>
/// Common chapter numbering/index information shared across chapter projections.
/// </summary>
public abstract class ChapterDescriptorBase : IChapterIndex
{
    public decimal? ChapterNumber { get; set; }
    public int Index { get; set; }
}
