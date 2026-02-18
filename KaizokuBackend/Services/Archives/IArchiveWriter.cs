namespace KaizokuBackend.Services.Archives;

/// <summary>
/// Interface for writing archive files (CBZ, PDF, etc.)
/// </summary>
public interface IArchiveWriter : IAsyncDisposable
{
    /// <summary>
    /// Writes an entry to the archive
    /// </summary>
    /// <param name="entryName">Name of the entry in the archive</param>
    /// <param name="content">Content stream to write</param>
    /// <param name="token">Cancellation token</param>
    Task WriteEntryAsync(string entryName, Stream content, CancellationToken token = default);

    /// <summary>
    /// Finalizes the archive (must be called before disposing)
    /// </summary>
    /// <param name="token">Cancellation token</param>
    Task FinalizeAsync(CancellationToken token = default);

    /// <summary>
    /// File extension for this archive type (including the dot)
    /// </summary>
    string FileExtension { get; }
}
