namespace KaizokuBackend.Models;

/// <summary>
/// Supported output archive formats for chapter downloads
/// </summary>
public enum ArchiveFormat
{
    /// <summary>
    /// Comic Book Zip format (default)
    /// </summary>
    Cbz = 0,

    /// <summary>
    /// Portable Document Format
    /// </summary>
    Pdf = 1
}
