namespace KaizokuBackend.Models;

[Flags]
public enum ArchiveCompare

{
    Equal = 0x1,
    MissingDB = 0x2,
    MissingArchive = 0x4,
    NotFound = 0x8,
}