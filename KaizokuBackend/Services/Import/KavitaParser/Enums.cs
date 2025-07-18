using System.ComponentModel;

namespace KaizokuBackend.Services.Import.KavitaParser;

public enum LibraryType
{
    /// <summary>
    /// Uses Manga regex for filename parsing
    /// </summary>
    [Description("Manga")]
    Manga = 0,
    /// <summary>
    /// Uses Comic regex for filename parsing
    /// </summary>
    [Description("Comic (Legacy)")]
    Comic = 1,
    /// <summary>
    /// Uses Manga regex for filename parsing also uses epub metadata
    /// </summary>
    [Description("Book")]
    Book = 2,
    /// <summary>
    /// Uses a different type of grouping and parsing mechanism
    /// </summary>
    [Description("Image")]
    Image = 3,
    /// <summary>
    /// Allows Books to Scrobble with AniList for Kavita+
    /// </summary>
    [Description("Light Novel")]
    LightNovel = 4,
    /// <summary>
    /// Uses Comic regex for filename parsing, uses Comic Vine type of Parsing
    /// </summary>
    [Description("Comic")]
    ComicVine = 5,
}
public enum MangaFormat
{
    /// <summary>
    /// Image file
    /// See <see cref="Parser.ImageFileExtensions"/> for supported extensions
    /// </summary>
    [Description("Image")]
    Image = 0,
    /// <summary>
    /// Archive based file
    /// See <see cref="Parser.ArchiveFileExtensions"/> for supported extensions
    /// </summary>
    [Description("Archive")]
    Archive = 1,
    /// <summary>
    /// Unknown
    /// </summary>
    /// <remarks>Default state for all files, but at end of processing, will never be Unknown.</remarks>
    [Description("Unknown")]
    Unknown = 2,
    /// <summary>
    /// EPUB File
    /// </summary>
    [Description("EPUB")]
    Epub = 3,
    /// <summary>
    /// PDF File
    /// </summary>
    [Description("PDF")]
    Pdf = 4
}