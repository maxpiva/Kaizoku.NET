using KaizokuBackend.Models;

namespace KaizokuBackend.Services.Archives;

/// <summary>
/// Factory for creating archive writers based on format
/// </summary>
public class ArchiveWriterFactory
{
    /// <summary>
    /// Creates an archive writer for the specified format
    /// </summary>
    /// <param name="format">Archive format to create</param>
    /// <param name="outputStream">Output stream to write to</param>
    /// <returns>Archive writer instance</returns>
    public IArchiveWriter Create(ArchiveFormat format, Stream outputStream)
    {
        return format switch
        {
            ArchiveFormat.Cbz => new CbzArchiveWriter(outputStream),
            ArchiveFormat.Pdf => new PdfArchiveWriter(outputStream),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported archive format")
        };
    }

    /// <summary>
    /// Gets the file extension for the specified format
    /// </summary>
    /// <param name="format">Archive format</param>
    /// <returns>File extension including the dot</returns>
    public static string GetExtension(ArchiveFormat format)
    {
        return format switch
        {
            ArchiveFormat.Cbz => ".cbz",
            ArchiveFormat.Pdf => ".pdf",
            _ => ".cbz"
        };
    }
}
